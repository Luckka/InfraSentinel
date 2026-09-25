using InfraSentinel.Core;
using InfraSentinel.Core.Architecture;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Roadmap;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class ArchitectureDefenseTests
{
    private static readonly ArchitectureDefenseValidator Validator = new();

    [Fact]
    public void ApprovedDecisionPassesAllRules()
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision()));

        Assert.Equal(ArchitectureDefenseStatus.Approved, result.Status);
        Assert.True(result.Approved);
        Assert.Empty(result.Findings);
        Assert.Equal(result.RulesEvaluated, result.RulesPassed);
    }

    [Fact]
    public void MissingAdrRequiresHumanWithHighFinding()
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision() with
        {
            AdrReference = null,
            AdrStatus = ArchitectureAdrStatus.Missing
        }));

        AssertHumanRequired(result, "adr-required", ArchitectureFindingSeverity.High);
    }

    [Theory]
    [InlineData(ArchitectureAdrStatus.Draft)]
    [InlineData(ArchitectureAdrStatus.Proposed)]
    [InlineData(ArchitectureAdrStatus.Rejected)]
    public void UnapprovedAdrRequiresHuman(ArchitectureAdrStatus status)
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision() with { AdrStatus = status }));

        AssertHumanRequired(result, "adr-approved", ArchitectureFindingSeverity.High);
    }

    [Fact]
    public void UnapprovedDecisionRequiresHuman()
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision() with
        {
            Status = ArchitectureDecisionStatus.Proposed
        }));

        AssertHumanRequired(result, "decision-approved", ArchitectureFindingSeverity.High);
    }

    [Fact]
    public void MissingAlternativesProducesExplainableFinding()
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision() with { AlternativesConsidered = [] }));

        AssertHumanRequired(result, "alternatives-required", ArchitectureFindingSeverity.High);
    }

    [Fact]
    public void MissingConsequencesProducesExplainableFinding()
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision() with { Consequences = [] }));

        AssertHumanRequired(result, "consequences-required", ArchitectureFindingSeverity.High);
    }

    [Fact]
    public void SensitiveDecisionWithoutEvidenceRequiresHuman()
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision() with { Evidence = [] }));

        AssertHumanRequired(result, "evidence-required-for-sensitive-decision", ArchitectureFindingSeverity.High);
    }

    [Fact]
    public void ExplicitHumanApprovalCannotBeAutomaticallyApproved()
    {
        var result = Validator.Evaluate(ArchitectureDecisionFixture.Single(ApprovedDecision() with { RequiresHumanApproval = true }));

        AssertHumanRequired(result, "human-approval-boundary", ArchitectureFindingSeverity.Critical);
    }

    [Fact]
    public void EvaluationIsDeterministic()
    {
        var fixture = ArchitectureDecisionFixture.Single(ApprovedDecision() with { Consequences = [] });

        var first = Validator.Evaluate(fixture);
        var second = Validator.Evaluate(fixture);

        Assert.Equal(first.DecisionId, second.DecisionId);
        Assert.Equal(first.RulesEvaluated, second.RulesEvaluated);
        Assert.Equal(first.RulesPassed, second.RulesPassed);
        Assert.Equal(first.Findings, second.Findings);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.RequiresHumanApproval, second.RequiresHumanApproval);
        Assert.Equal(first.Justification, second.Justification);
    }

    [Fact]
    public async Task RunnerPersistsExplainableArtifactInSentinelDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var runner = configuration.CreateArchitectureDefenseRunner(ArchitectureDecisionFixture.Single(ApprovedDecision()));

        var results = await runner.RunAsync();

        Assert.True(results.Single().Passed);
        Assert.True(File.Exists(configuration.ArchitectureDefenseArtifactPath));
        var json = await File.ReadAllTextAsync(configuration.ArchitectureDefenseArtifactPath);
        Assert.Contains("decision-001", json, StringComparison.Ordinal);
        Assert.Contains("rulesEvaluated", json, StringComparison.Ordinal);
        Assert.Contains("high", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".ai-runs")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, configuration.RunsDirectory, "architecture-defense.json")));
    }

    [Fact]
    public async Task EngineHostRunsApprovedArchitectureDefenseAndMilestoneRequiresExplicitApproval()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var fixture = ArchitectureDecisionFixture.Single(ApprovedDecision());
        var host = CreateHost(configuration, configuration.CreateArchitectureDefenseRunner(fixture));

        var run = await host.RunAsync(new DevelopmentTask("ARCH-DEFENSE-001", "Architecture defense", "Run the local architecture defense gate."));
        Assert.Equal(WorkflowState.Approved, run.State);
        Assert.Equal("PASS", run.FinalDecision);

        var milestoneHost = CreateHost(configuration, configuration.CreateArchitectureDefenseRunner(fixture));
        var completed = await milestoneHost.RunMilestoneAsync("architecture-defense-validation");
        Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, completed.Status);
        var approved = await milestoneHost.ApproveMilestoneAsync("architecture-defense-validation");
        Assert.Equal(MilestoneRuntimeStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task EngineHostConvertsDefenseFailureToHumanRequiredWithoutAutomaticApproval()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var fixture = ArchitectureDecisionFixture.Single(ApprovedDecision() with { AdrStatus = ArchitectureAdrStatus.Proposed });
        var host = CreateHost(configuration, configuration.CreateArchitectureDefenseRunner(fixture), validationRemediationCycles: 0);

        var result = await host.RunAsync(new DevelopmentTask("ARCH-DEFENSE-002", "Architecture defense", "Require human review for a proposed ADR."));

        Assert.Equal(WorkflowState.HumanRequired, result.State);
        Assert.Equal("HUMAN_REQUIRED", result.FinalDecision);
        Assert.True(File.Exists(configuration.ArchitectureDefenseArtifactPath));
    }

    private static EngineHost CreateHost(
        IAEngineConsumerConfiguration configuration,
        IValidationRunner validation,
        int validationRemediationCycles = 5)
    {
        var options = configuration.CreateOptions(validationRemediationCycles: validationRemediationCycles);
        return EngineHost.Create(new EngineHostContext
        {
            ProjectId = IAEngineConsumerConfiguration.ProjectId,
            WorkspaceRoot = configuration.WorkspaceRoot,
            Options = options,
            Composition = configuration.Composition,
            Components = new("local-router", "local-implementation", "local-review", "validation"),
            RegisterComponents = builder => builder
                .RegisterProvider<ITaskRouter>("local-router", () => new LocalRouter())
                .RegisterProvider<IImplementationAgent>("local-implementation", () => new LocalImplementation())
                .RegisterProvider<IReviewAgent>("local-review", () => new LocalReview())
                .RegisterValidator<IValidationRunner>("local-validator", () => validation)
                .RegisterPolicy("local-policy", () => new LocalPolicy())
                .RegisterCapability<IValidationRunner>("validation", () => validation),
            RunStore = new RunStore(configuration.WorkspaceRoot, configuration.RunsDirectory),
            Git = new LocalGit(configuration.WorkspaceRoot),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory
        });
    }

    private static ArchitectureDecision ApprovedDecision() => new(
        "decision-001",
        "Use a local deterministic architecture gate",
        "InfraSentinel needs a first local architecture defense boundary.",
        "Architecture decisions require explainable evidence before implementation.",
        "Keep domain rules in InfraSentinel and execute them through IAEngine validation.",
        ["Put the rules in IAEngine.Core", "Use an external scanning provider"],
        ["InfraSentinel owns the semantics", "IAEngine remains generic", "Human approval remains explicit"],
        ["ArchitectureDefenseTests", "EngineHost integration test"],
        "docs/adr/0006-architecture-defense-gate.md",
        ArchitectureAdrStatus.Approved,
        ArchitectureDecisionStatus.Approved,
        false,
        ArchitectureFindingSeverity.High);

    private static void AssertHumanRequired(ArchitectureDefenseEvaluation result, string ruleId, ArchitectureFindingSeverity severity)
    {
        Assert.Equal(ArchitectureDefenseStatus.HumanRequired, result.Status);
        Assert.True(result.RequiresHumanApproval);
        var finding = Assert.Single(result.Findings, x => x.RuleId == ruleId);
        Assert.Equal(severity, finding.Severity);
    }

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("architecture-defense", ["architecture"], "low", "high", [], [], "local", "deterministic local route", true));

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

    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));

        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m8-architecture-defense-gate");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local-fake");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("infrasentinel-architecture-defense-");
        public string Path => directory.FullName;
        public void Dispose() => directory.Delete(true);
    }
}
