using IAEngine.Core.Git;
using InfraSentinel.Core;
using InfraSentinel.Core.Observability;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Roadmap;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class ObservabilityEvidenceTests
{
    [Fact]
    public void RetryAndRemediationEvidenceIsRecordedWithoutDuplicatingFindings()
    {
        var aggregator = CreateAggregator();
        var finding = new ObservabilityEvidenceFinding("OBS-VAL-001", "fixture", "High", "Synthetic finding", ["fixture evidence"]);

        aggregator.RecordValidation("OBS-002", false, "Validation failed.", [finding]);
        aggregator.RecordValidation("OBS-002", true, "Validation passed.", []);

        var artifact = aggregator.BuildArtifact();

        Assert.Equal(2, artifact.Attempts);
        Assert.Equal(1, artifact.Retries);
        Assert.Equal(1, artifact.RemediationCount);
        Assert.Single(artifact.Findings);
        Assert.Contains(artifact.Events, item => item.Kind == ObservabilityEvidenceEventKind.Retry);
        Assert.Contains(artifact.Events, item => item.Kind == ObservabilityEvidenceEventKind.Remediation);
    }

    [Fact]
    public void IncompleteEvidenceProducesExplainableValidationFailure()
    {
        var aggregator = CreateAggregator(["OBS-001", "OBS-003"]);
        aggregator.RecordTask("OBS-001");
        aggregator.RecordValidation("OBS-002", false, "Validation failed.",
        [
            new("OBS-VAL-001", "fixture", "Critical", "Critical synthetic finding", ["fixture evidence"])
        ]);

        var result = new ObservabilityEvidenceValidator().Validate(aggregator.Snapshot());

        Assert.False(result.Passed);
        Assert.Contains("task-evidence-present", result.RulesEvaluated);
        Assert.Contains(result.Findings, finding => finding.RuleId == "task-evidence-present");
        Assert.Contains(result.Findings, finding => finding.RuleId == "review-evidence-present");
    }

    [Fact]
    public void EquivalentEvidenceHasStableLogicalOrdering()
    {
        var first = CreateAggregator();
        var second = CreateAggregator();
        foreach (var aggregator in new[] { first, second })
        {
            aggregator.RecordTask("OBS-002");
            aggregator.RecordTask("OBS-001");
            aggregator.RecordValidation("OBS-002", true, "Validation passed.");
            aggregator.RecordReview("OBS-004", true, "Review passed.");
        }

        var firstArtifact = first.BuildArtifact();
        var secondArtifact = second.BuildArtifact();

        Assert.Equal(firstArtifact.ValidationsExecuted, secondArtifact.ValidationsExecuted);
        Assert.Equal(firstArtifact.Severities, secondArtifact.Severities);
        Assert.Equal(
            firstArtifact.Events.Select(item => (item.Kind, item.TaskId, item.Detail, item.Attempt)),
            secondArtifact.Events.Select(item => (item.Kind, item.TaskId, item.Detail, item.Attempt)));
        Assert.Equal(
            firstArtifact.Events.Select(item => item.Findings),
            secondArtifact.Events.Select(item => item.Findings));
    }

    [Fact]
    public async Task EngineHostProducesEvidenceAndCheckpointOnlyAfterApproval()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var taskIds = new[] { "OBS-001", "OBS-002", "OBS-003", "OBS-004", "OBS-005", "OBS-006" };
        var aggregator = configuration.CreateEvidenceAggregator(
            "m13-execution",
            "sentinel-observability-evidence-gate",
            taskIds,
            "observability-safe",
            "1",
            "feature/m13-observability-evidence-gate");
        foreach (var taskId in taskIds) aggregator.RecordTask(taskId);

        var writer = configuration.CreateEvidenceWriter();
        var coordinator = new RecordingCoordinator();
        var validation = new ObservabilityEvidenceValidationRunner(new PassingValidation(), aggregator, "OBS-002");
        var review = new ObservabilityEvidenceReviewAgent(new PassingReview(), aggregator, "OBS-004");
        var checkpoint = new ObservabilityEvidenceCheckpointCoordinator(coordinator, aggregator, writer);
        var host = CreateHost(configuration, validation, review, checkpoint, coordinator);

        var awaitingApproval = await host.RunMilestoneAsync("sentinel-observability-evidence-gate");

        Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, awaitingApproval.Status);
        Assert.Empty(coordinator.CommitRequests);
        Assert.False(File.Exists(configuration.ObservabilityEvidenceArtifactPath));

        aggregator.RecordApproval("OBS-006", true, "Explicit human approval recorded.");
        var approved = await host.ApproveMilestoneAsync("sentinel-observability-evidence-gate");

        Assert.Equal(MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.Single(coordinator.CommitRequests);
        Assert.StartsWith("test: ", coordinator.CommitRequests[0].ProposedCommitMessage);
        Assert.True(File.Exists(configuration.ObservabilityEvidenceArtifactPath));

        var artifact = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(configuration.ObservabilityEvidenceArtifactPath));
        Assert.Equal("1.0", artifact.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("m13-sha", artifact.RootElement.GetProperty("commitSha").GetString());
        Assert.False(artifact.RootElement.GetProperty("pushPerformed").GetBoolean());
        Assert.False(artifact.RootElement.GetProperty("mergePerformed").GetBoolean());
        Assert.Contains("Checkpoint", artifact.RootElement.GetProperty("events").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HumanRequiredEvidenceBlocksCheckpointAndPreservesArtifact()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var aggregator = configuration.CreateEvidenceAggregator("critical-execution", "sentinel-observability-evidence-gate", ["OBS-001"], "critical", "1", "feature/m13-observability-evidence-gate");
        aggregator.RecordTask("OBS-001");
        aggregator.RecordValidation("OBS-001", false, "Critical finding requires human review.",
        [new("OBS-CRITICAL", "secret", "Critical", "Synthetic critical finding", ["local fixture"]) ]);
        aggregator.RecordReview("OBS-004", false, "Review blocked by critical finding.");
        aggregator.RecordApproval("OBS-006", false, "Human approval required.");
        aggregator.RecordCheckpoint("OBS-006", new(GitCheckpointDecisionStatus.HumanRequired, "Human approval is required.", ["critical-finding"], ["fixture.json"], "feature/m13-observability-evidence-gate", "feat: blocked checkpoint", false, true), GitCheckpointResult.Blocked("Human approval is required.", new("infra-sentinel", workspace.Root, GitCheckpointScope.Milestone, "sentinel-observability-evidence-gate", "OBS-006", "feature/m13-observability-evidence-gate", "feat: blocked checkpoint", GitCheckpointState.HumanRequired, false, false, true, true, true, true, false, true, true, ["fixture.json"], ["fixture.json"], true, true, false, false)));

        await configuration.CreateEvidenceWriter().WriteAsync(aggregator, CancellationToken.None);

        var artifact = aggregator.BuildArtifact();
        Assert.Equal(ObservabilityEvidenceStatus.HumanRequired, artifact.Status);
        Assert.Null(artifact.CommitSha);
        Assert.False(artifact.PushPerformed);
        Assert.False(artifact.MergePerformed);
        Assert.True(File.Exists(configuration.ObservabilityEvidenceArtifactPath));
    }

    [Fact]
    public void CheckpointEvidenceRecordsDiffBlockWithoutCommit()
    {
        var aggregator = CreateAggregator();
        var decision = new GitCheckpointDecision(GitCheckpointDecisionStatus.Blocked, "Unrelated files detected.", ["unrelated-changes"], ["unrelated.txt"], "feature/m13-observability-evidence-gate", "docs: record evidence", false, false);

        aggregator.RecordCheckpoint("OBS-006", decision, GitCheckpointResult.Blocked(decision.Reason, new("infra-sentinel", Directory.GetCurrentDirectory(), GitCheckpointScope.Milestone, "sentinel-observability-evidence-gate", "OBS-006", decision.Branch, decision.ProposedCommitMessage, GitCheckpointState.Approved, true, true, true, true, true, false, true, true, true, ["unrelated.txt"], ["fixture.txt"], true, true, false, false)));

        var artifact = aggregator.BuildArtifact();
        Assert.Equal(ObservabilityEvidenceStatus.Blocked, artifact.Status);
        Assert.Contains("unrelated-changes", artifact.CheckpointDecision!.Evidence);
        Assert.Null(artifact.CommitSha);
    }

    private static ObservabilityEvidenceAggregator CreateAggregator(IReadOnlyList<string>? taskIds = null)
        => new("execution", "infra-sentinel", "sentinel-observability-evidence-gate", taskIds ?? ["OBS-001", "OBS-002", "OBS-003", "OBS-004", "OBS-005", "OBS-006"], "fixture", "1", "a7b387e", "feature/m13-observability-evidence-gate");

    private static EngineHost CreateHost(IAEngineConsumerConfiguration configuration, IValidationRunner validation, IReviewAgent review, IGitCheckpointCoordinator checkpoint, RecordingCoordinator recording)
        => EngineHost.Create(new EngineHostContext
        {
            ProjectId = IAEngineConsumerConfiguration.ProjectId,
            WorkspaceRoot = configuration.WorkspaceRoot,
            Options = configuration.CreateOptions(),
            Composition = configuration.Composition,
            Components = new("local-router", "local-implementation", "local-review", "validation"),
            RegisterComponents = builder => builder
                .RegisterProvider<ITaskRouter>("local-router", () => new LocalRouter())
                .RegisterProvider<IImplementationAgent>("local-implementation", () => new LocalImplementation())
                .RegisterProvider<IReviewAgent>("local-review", () => review)
                .RegisterValidator<IValidationRunner>("local-validator", () => validation)
                .RegisterCapability<IValidationRunner>("validation", () => validation)
                .RegisterPolicy("local-policy", () => new LocalPolicy()),
            RunStore = new RunStore(configuration.WorkspaceRoot, configuration.RunsDirectory),
            Git = new LocalGit(configuration.WorkspaceRoot),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory,
            CheckpointCoordinator = checkpoint,
            CheckpointRequestSource = configuration.CreateCheckpointRequestSource("feature/m13-observability-evidence-gate", ["fixture.txt"], "test: record observability evidence")
        });

    private sealed class PassingValidation : IValidationRunner
    {
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationResult>>([new("observability", true, new ProcessResult("local", 0, "", "", TimeSpan.Zero))]);
    }

    private sealed class PassingReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "Evidence review passed."));
    }

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("synthetic", ["local"], "low", "low", [], [], "local", "local", true));

        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "local"));
    }

    private sealed class LocalImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "local", []));

        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => Task.FromResult(new ImplementationResult(true, "local remediation", []));
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m13-observability-evidence-gate");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }

    private sealed class RecordingCoordinator : IGitCheckpointCoordinator
    {
        public List<GitCheckpointRequest> CommitRequests { get; } = [];

        public Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(GitCheckpointPolicy.Evaluate(request));

        public Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
        {
            CommitRequests.Add(request);
            return Task.FromResult(new GitCheckpointResult(true, "m13-sha", request.ProposedCommitMessage, request.CurrentBranch, request.ExpectedFiles, DateTimeOffset.UtcNow, null, true, false, false));
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("infrasentinel-m13-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
