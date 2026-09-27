using IAEngine.Core.Git;
using InfraSentinel.Core;
using InfraSentinel.Core.Cloud;
using InfraSentinel.Core.IaC;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class CloudObservationTests
{
    [Fact]
    public async Task SyntheticProviderReturnsDeterministicSafeSnapshot()
    {
        var snapshot = SafeSnapshot();
        var provider = new SyntheticCloudObservationProvider(snapshot);
        var request = Request();

        var first = await provider.GetSnapshotAsync(request);
        var second = await provider.GetSnapshotAsync(request);

        Assert.Equal(CloudCollectionStatus.Collected, first.Status);
        Assert.True(first.IsSynthetic);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first.Snapshot), System.Text.Json.JsonSerializer.Serialize(second.Snapshot));
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task McpBoundaryAllowsOnlyReadOnlyToolsAndRedactsArguments()
    {
        var client = new SyntheticMcpReadOnlyToolClient(new Dictionary<string, string> { ["bucket"] = "synthetic" });
        await client.ListResourcesAsync(new Dictionary<string, string> { ["token"] = "secret-token" });
        await client.GetResourceConfigurationAsync("bucket");
        await client.GetSecurityPostureAsync("bucket");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.InvokeAsync("delete_resource", new Dictionary<string, string> { ["password"] = "secret" }));

        Assert.Equal(4, client.Calls.Count);
        Assert.Contains(client.Calls, call => call.ToolName == "delete_resource" && !call.Allowed);
        Assert.Contains(client.Calls[0].Arguments.Values, value => value == "[REDACTED]");
    }

    [Fact]
    public void SafeSnapshotMatchesIaC()
    {
        var evaluation = new IacCloudSnapshotComparator().Compare("safe", Desired(), SafeSnapshot());
        Assert.Equal(CloudObservationStatus.Approved, evaluation.Status);
        Assert.Empty(evaluation.Findings);
    }

    [Fact]
    public void DivergentSnapshotProducesExplainableFindings()
    {
        var evaluation = new IacCloudSnapshotComparator().Compare("drift", Desired(), DivergentSnapshot());
        Assert.Equal(CloudObservationStatus.Unknown, evaluation.Status);
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "CLOUD-LOGGING-DRIFT");
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "CLOUD-OWNER-MISSING");
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "CLOUD-RESOURCE-EXTRA");
    }

    [Fact]
    public void CriticalSnapshotRequiresHumanApproval()
    {
        var evaluation = new IacCloudSnapshotComparator().Compare("critical", Desired(), CriticalSnapshot());
        Assert.Equal(CloudObservationStatus.HumanRequired, evaluation.Status);
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "CLOUD-PUBLIC-DRIFT");
        Assert.Contains(evaluation.Findings, finding => finding.Severity == CloudFindingSeverity.Critical);
    }

    [Fact]
    public void UnknownSnapshotDoesNotPassSilently()
    {
        var evaluation = new IacCloudSnapshotComparator().Compare("unknown", Desired(), UnknownSnapshot());
        Assert.NotEqual(CloudObservationStatus.Approved, evaluation.Status);
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "CLOUD-UNKNOWN");
        Assert.NotEmpty(evaluation.Limitations);
    }

    [Fact]
    public async Task ProviderFailureTimeoutAndCancellationAreExplicit()
    {
        var failed = await new SyntheticCloudObservationProvider(SafeSnapshot(), fail: true).GetSnapshotAsync(Request());
        var timedOut = await new SyntheticCloudObservationProvider(SafeSnapshot(), timeout: true).GetSnapshotAsync(Request() with { Timeout = TimeSpan.FromMilliseconds(10) });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Equal(CloudCollectionStatus.Failed, failed.Status);
        Assert.Equal(CloudCollectionStatus.TimedOut, timedOut.Status);
        await Assert.ThrowsAsync<OperationCanceledException>(() => new SyntheticCloudObservationProvider(SafeSnapshot()).GetSnapshotAsync(Request(), cancelled.Token));
    }

    [Fact]
    public async Task ObservationRunnerRetriesProviderFailureWithBoundedAttempts()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new CloudObservationEvidence("retry", "infra-sentinel", "read-only-cloud-observation-validation", "feature/m16-read-only-cloud-observation");
        var runner = new CloudObservationValidationRunner(
            new SequenceProvider([new(CloudCollectionStatus.Failed, null, ["temporary"], "temporary failure", true), new(CloudCollectionStatus.Collected, SafeSnapshot(), [], "collected", true)]),
            Request() with { MaxAttempts = 2 }, Desired(), "retry", new IacCloudSnapshotComparator(), evidence, configuration.CreateCloudObservationArtifactWriter());

        var results = await runner.RunAsync();

        Assert.True(results.Single().Passed);
        Assert.Equal(1, evidence.BuildArtifact().RetryAttempts);
    }

    [Fact]
    public async Task ObservationRunnerPersistsArtifactAndRecordsProviderFailure()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new CloudObservationEvidence("failed", "infra-sentinel", "read-only-cloud-observation-validation", "feature/m16-read-only-cloud-observation");
        var writer = configuration.CreateCloudObservationArtifactWriter();
        var runner = new CloudObservationValidationRunner(new SyntheticCloudObservationProvider(SafeSnapshot(), fail: true), Request(), Desired(), "failed", new IacCloudSnapshotComparator(), evidence, writer);

        var results = await runner.RunAsync();
        var artifact = evidence.BuildArtifact();

        Assert.False(results.Single().Passed);
        Assert.Equal(CloudObservationStatus.Failed, artifact.Status);
        Assert.True(File.Exists(configuration.CloudObservationArtifactPath));
        Assert.False(artifact.PushPerformed);
        Assert.False(artifact.MergePerformed);
    }

    [Fact]
    public async Task EngineHostRunsSafeObservationAndAllowsCheckpointAfterApproval()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new CloudObservationEvidence("m16-execution", "infra-sentinel", "read-only-cloud-observation-validation", "feature/m16-read-only-cloud-observation");
        var writer = configuration.CreateCloudObservationArtifactWriter();
        var runner = new CloudObservationValidationRunner(new SyntheticCloudObservationProvider(SafeSnapshot()), Request(), Desired(), "m16-execution", new IacCloudSnapshotComparator(), evidence, writer);
        var coordinator = new RecordingCoordinator();
        var host = EngineHost.Create(new EngineHostContext
        {
            ProjectId = IAEngineConsumerConfiguration.ProjectId,
            WorkspaceRoot = workspace.Root,
            Options = configuration.CreateOptions(),
            Composition = configuration.Composition,
            Components = new("local-router", "local-implementation", "local-review", "validation"),
            RegisterComponents = builder => builder
                .RegisterProvider<ITaskRouter>("local-router", () => new LocalRouter())
                .RegisterProvider<IImplementationAgent>("local-implementation", () => new LocalImplementation())
                .RegisterProvider<IReviewAgent>("local-review", () => new CloudObservationReviewAgent(new LocalReview(), evidence))
                .RegisterValidator<IValidationRunner>("local-validator", () => runner)
                .RegisterCapability<IValidationRunner>("validation", () => runner)
                .RegisterPolicy("local-policy", () => new LocalPolicy()),
            RunStore = new RunStore(workspace.Root, configuration.RunsDirectory),
            Git = new LocalGit(workspace.Root),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory,
            CheckpointCoordinator = new CloudObservationCheckpointCoordinator(coordinator, evidence, writer),
            CheckpointRequestSource = new CloudObservationCheckpointRequestSource(configuration.CreateCheckpointRequestSource("feature/m16-read-only-cloud-observation", ["snapshot.json"], "test: validate read-only cloud observation"), evidence)
        });

        var awaiting = await host.RunMilestoneAsync("read-only-cloud-observation-validation");
        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.CompleteAwaitingApproval, awaiting.Status);
        Assert.Empty(coordinator.Commits);
        var approved = await host.ApproveMilestoneAsync("read-only-cloud-observation-validation");

        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.Single(coordinator.Commits);
        var artifact = await File.ReadAllTextAsync(configuration.CloudObservationArtifactPath);
        Assert.Contains("read-only-cloud-observation-validation", artifact, StringComparison.Ordinal);
        Assert.Contains("m16-sha", artifact, StringComparison.Ordinal);
        Assert.Contains("\"pushPerformed\": false", artifact, StringComparison.Ordinal);
        Assert.Contains("\"mergePerformed\": false", artifact, StringComparison.Ordinal);
    }

    private static CloudObservationRequest Request() => new("infra-sentinel", "production", "us-east-1", ["bucket", "network", "iam"], [], TimeSpan.FromSeconds(1), 20);
    private static NeutralIaCModel Desired() => new("infra-sentinel", "terraform", "1.0", [new("bucket", "storage", "resource", [], false, [], true, true, true, true, false, "platform", "production", ["read"], "/main.tf", 1, ["main.tf:1"], [])], [], [], [], [], ["/main.tf"], [], []);
    private static CloudInfrastructureSnapshot SafeSnapshot() => new("safe-snapshot", "1", "synthetic", "demo", "us-east-1", "production", DateTimeOffset.UnixEpoch, CloudObservationSourceType.Synthetic, [new("bucket", "storage", "us-east-1", "production", false, true, true, true, "platform", ["read"], [], ["snapshot:bucket"], [])], [], [], [], [], [], [], [], [], CloudCollectionStatus.Collected);
    private static CloudInfrastructureSnapshot DivergentSnapshot() => SafeSnapshot() with { Resources = [new("bucket", "storage", "us-east-1", "production", false, true, false, true, null, ["read"], [], ["snapshot:bucket"], []), new("extra", "storage", "us-east-1", "production", false, true, true, true, "platform", [], [], ["snapshot:extra"], [])] };
    private static CloudInfrastructureSnapshot CriticalSnapshot() => SafeSnapshot() with { Resources = [new("bucket", "storage", "us-east-1", "production", true, false, false, false, null, ["*"], [], ["snapshot:bucket"], [])] };
    private static CloudInfrastructureSnapshot UnknownSnapshot() => SafeSnapshot() with { Limitations = ["security posture unavailable"], Resources = [new("bucket", "storage", "us-east-1", "production", null, null, null, null, null, [], [], [], ["unknown"]) ] };

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default) => Task.FromResult(new RoutingResult("synthetic", ["local"], "low", "low", [], [], "local", "local", true));
        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, true, "local"));
    }
    private sealed class LocalImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default) => Task.FromResult(new ImplementationResult(true, "local", []));
        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0) => Task.FromResult(new ImplementationResult(true, "local", []));
    }
    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default) => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));
    }
    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;
    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m16-read-only-cloud-observation");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }
    private sealed class RecordingCoordinator : IGitCheckpointCoordinator
    {
        public List<GitCheckpointRequest> Commits { get; } = [];
        public Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default) => Task.FromResult(GitCheckpointPolicy.Evaluate(request));
        public Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default) { Commits.Add(request); return Task.FromResult(new GitCheckpointResult(true, "m16-sha", request.ProposedCommitMessage, request.CurrentBranch, request.ExpectedFiles, DateTimeOffset.UnixEpoch, null, true, false, false)); }
    }
    private sealed class SequenceProvider(IReadOnlyList<CloudObservationResult> results) : ICloudObservationProvider
    {
        private int index;
        public string ProviderId => "synthetic-sequence";
        public CloudObservationSourceType SourceType => CloudObservationSourceType.Synthetic;
        public Task<CloudObservationResult> GetSnapshotAsync(CloudObservationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = results[Math.Min(index++, results.Count - 1)];
            return Task.FromResult(result);
        }
    }
    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("infrasentinel-m16-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
