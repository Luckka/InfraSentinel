using InfraSentinel.Core;
using InfraSentinel.Core.Resilience;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Roadmap;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class ResilienceContractTests
{
    private static readonly ResilienceContractValidator Validator = new();

    [Fact]
    public void CompleteContractIsApproved()
    {
        var result = Validator.Evaluate(ValidFixture());

        Assert.True(result.Approved);
        Assert.Equal("Approved", result.Status);
        Assert.Empty(result.Findings);
        Assert.Equal(result.RulesEvaluated, result.RulesPassed);
    }

    [Fact]
    public void MissingTimeoutIsHighAndHumanRequired()
    {
        var result = Validator.Evaluate(With(component => component with
        {
            Dependencies = [component.Dependencies[0] with { TimeoutSeconds = null }]
        }));

        AssertFinding(result, "timeout-required", ResilienceFindingSeverity.High);
    }

    [Fact]
    public void InfiniteRetryIsCritical()
    {
        var result = Validator.Evaluate(With(component => component with
        {
            RetryPolicy = component.RetryPolicy! with { MaxAttempts = null }
        }));

        AssertFinding(result, "retry-limited", ResilienceFindingSeverity.Critical);
    }

    [Fact]
    public void RetryWithoutBackoffIsHigh()
    {
        var result = Validator.Evaluate(With(component => component with
        {
            RetryPolicy = component.RetryPolicy! with { Backoff = ResilienceBackoffStrategy.None }
        }));

        AssertFinding(result, "retry-limited", ResilienceFindingSeverity.High);
    }

    [Fact]
    public void MissingIdempotencyIsHigh()
    {
        var result = Validator.Evaluate(With(component => component with { Idempotency = null }));

        AssertFinding(result, "idempotency-required", ResilienceFindingSeverity.High);
    }

    [Fact]
    public void DistributedOperationWithoutRecoveryIsHigh()
    {
        var result = Validator.Evaluate(With(component => component with { RecoveryStrategy = null }));

        AssertFinding(result, "recovery-required", ResilienceFindingSeverity.High);
    }

    [Fact]
    public void MissingFailureDestinationIsMediumForLowSeverityComponent()
    {
        var result = Validator.Evaluate(With(component => component with
        {
            FailureDestination = null,
            Severity = ResilienceFindingSeverity.Low
        }));

        AssertFinding(result, "failure-destination-required", ResilienceFindingSeverity.Medium);
    }

    [Fact]
    public void MissingObservabilityRequiresHumanReview()
    {
        var result = Validator.Evaluate(With(component => component with { ObservabilitySignals = [] }));

        AssertFinding(result, "observability-minimum", ResilienceFindingSeverity.Medium);
        Assert.True(result.RequiresHumanApproval);
    }

    [Fact]
    public void IndiscriminateAuthenticationRetryIsRejected()
    {
        var result = Validator.Evaluate(With(component => component with
        {
            RetryPolicy = component.RetryPolicy! with
            {
                RetryableErrors = [ResilienceErrorKind.Temporary, ResilienceErrorKind.Authentication]
            }
        }));

        Assert.False(result.Approved);
        Assert.Contains(result.Findings, finding => finding.RuleId == "error-classification-explicit"
            && finding.Severity == ResilienceFindingSeverity.High
            && finding.Explanation.Contains("indiscriminately", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingErrorClassificationIsHigh()
    {
        var result = Validator.Evaluate(With(component => component with
        {
            ErrorClassifications = component.ErrorClassifications.Where(error => error.Kind != ResilienceErrorKind.Unknown).ToArray()
        }));

        AssertFinding(result, "error-classification-explicit", ResilienceFindingSeverity.High);
    }

    [Fact]
    public void CriticalDependencyWithoutOutageBehaviorIsHigh()
    {
        var result = Validator.Evaluate(With(component => component with
        {
            Dependencies = [component.Dependencies[0] with { OutageBehavior = null }]
        }));

        AssertFinding(result, "critical-dependency-behavior", ResilienceFindingSeverity.High);
    }

    [Fact]
    public void ProposedContractCannotPass()
    {
        var result = Validator.Evaluate(With(component => component with { Status = ResilienceContractStatus.Proposed }));

        AssertFinding(result, "contract-approved", ResilienceFindingSeverity.High);
    }

    [Fact]
    public void ExplicitHumanApprovalCannotPass()
    {
        var result = Validator.Evaluate(With(component => component with { RequiresHumanApproval = true }));

        AssertFinding(result, "human-approval-boundary", ResilienceFindingSeverity.Critical);
    }

    [Fact]
    public void EvaluationIsDeterministic()
    {
        var fixture = With(component => component with { FailureDestination = null });

        var first = Validator.Evaluate(fixture);
        var second = Validator.Evaluate(fixture);

        Assert.Equal(first.ExecutionId, second.ExecutionId);
        Assert.Equal(first.ComponentsEvaluated, second.ComponentsEvaluated);
        Assert.Equal(first.RulesEvaluated, second.RulesEvaluated);
        Assert.Equal(first.RulesPassed, second.RulesPassed);
        Assert.Equal(first.Findings, second.Findings);
        Assert.Equal(first.HighestSeverity, second.HighestSeverity);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Justification, second.Justification);
    }

    [Fact]
    public async Task RunnerWritesExplainableArtifactToSentinelDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var runner = configuration.CreateResilienceRunner(ValidFixture());

        var result = await runner.RunAsync();

        Assert.True(result.Single().Passed);
        Assert.True(File.Exists(configuration.ResilienceContractArtifactPath));
        var artifact = await File.ReadAllTextAsync(configuration.ResilienceContractArtifactPath);
        Assert.Contains("resilience-fixture-001", artifact, StringComparison.Ordinal);
        Assert.Contains("timeout-required", artifact, StringComparison.Ordinal);
        Assert.Contains("approved", artifact, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".ai-runs")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".ai-state")));
    }

    [Fact]
    public async Task EngineHostRunsApprovedResilienceContract()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var host = CreateHost(configuration, configuration.CreateResilienceRunner(ValidFixture()));

        var result = await host.RunAsync(new DevelopmentTask("RESILIENCE-001", "Resilience contract", "Validate local resilience contract."));

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Equal("PASS", result.FinalDecision);
        Assert.True(File.Exists(configuration.ResilienceContractArtifactPath));
    }

    [Fact]
    public async Task EngineHostConvertsInvalidResilienceContractToHumanRequired()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var invalid = With(component => component with { RetryPolicy = component.RetryPolicy! with { MaxAttempts = null } });
        var host = CreateHost(configuration, configuration.CreateResilienceRunner(invalid), validationRemediationCycles: 0);

        var result = await host.RunAsync(new DevelopmentTask("RESILIENCE-002", "Resilience contract", "Require human review for infinite retry."));

        Assert.Equal(WorkflowState.HumanRequired, result.State);
        Assert.Equal("HUMAN_REQUIRED", result.FinalDecision);
    }

    [Fact]
    public async Task ResilienceMilestoneCompletesAndRequiresExplicitApproval()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var host = CreateHost(configuration, configuration.CreateResilienceRunner(ValidFixture()));

        var completed = await host.RunMilestoneAsync("resilience-contract-validation");
        Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, completed.Status);
        Assert.Equal(5, completed.CompletedTasks);

        var approved = await host.ApproveMilestoneAsync("resilience-contract-validation");
        Assert.Equal(MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.True(File.Exists(configuration.ResilienceContractArtifactPath));
    }

    [Fact]
    public async Task ResilienceMilestoneStopsAtHumanRequired()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var invalid = With(component => component with { Idempotency = null });
        var host = CreateHost(configuration, configuration.CreateResilienceRunner(invalid), validationRemediationCycles: 0);

        var result = await host.RunMilestoneAsync("resilience-contract-validation");

        Assert.Equal(MilestoneRuntimeStatus.HumanRequired, result.Status);
        Assert.Equal(0, result.CompletedTasks);
    }

    [Fact]
    public async Task ResilienceValidationRecoversWhenTheNextValidationPasses()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Path);
        var invalid = With(component => component with { RetryPolicy = component.RetryPolicy! with { MaxAttempts = null } });
        var runner = new RecoveringRunner(configuration.CreateResilienceRunner(invalid), configuration.CreateResilienceRunner(ValidFixture()));
        var host = CreateHost(configuration, runner, validationRemediationCycles: 1);

        var result = await host.RunAsync(new DevelopmentTask("RESILIENCE-003", "Resilience recovery", "Recover after a transient local validation result."));

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Equal("PASS", result.FinalDecision);
        Assert.Equal(2, runner.Calls);
    }

    private static ResilienceContractFixture ValidFixture() => new(
        "resilience-fixture-001",
        "1",
        [new ResilienceComponent(
            "checkout-worker",
            "checkout-service",
            ResilienceOperationType.Distributed,
            [new ResilienceDependency("payments", "payments provider", true, 30, "pending-state")],
            new ResilienceRetryPolicy(3, ResilienceBackoffStrategy.ExponentialWithJitter, [ResilienceErrorKind.Temporary, ResilienceErrorKind.Timeout]),
            true,
            ResilienceIdempotencyStrategy.IdempotencyKey,
            "reconciliation-worker",
            "checkout-quarantine",
            ["structured-logs", "metrics", "traces", "correlation-id", "alarm"],
            AllErrorClassifications(),
            ResilienceContractStatus.Approved,
            ["ResilienceContractTests", "local-fixture-evidence"],
            ResilienceFindingSeverity.High,
            false)],
        "resilience-execution-001");

    private static IReadOnlyList<ResilienceErrorClassification> AllErrorClassifications() =>
    [
        new(ResilienceErrorKind.Temporary, true, "bounded retry"),
        new(ResilienceErrorKind.Timeout, true, "bounded retry"),
        new(ResilienceErrorKind.Authentication, false, "surface for human review"),
        new(ResilienceErrorKind.Validation, false, "reject without retry"),
        new(ResilienceErrorKind.Definitive, false, "route to quarantine"),
        new(ResilienceErrorKind.Unknown, false, "hold for diagnosis")
    ];

    private static ResilienceContractFixture With(Func<ResilienceComponent, ResilienceComponent> change)
    {
        var fixture = ValidFixture();
        return fixture with { Components = [change(fixture.Components[0])] };
    }

    private static void AssertFinding(ResilienceEvaluation evaluation, string ruleId, ResilienceFindingSeverity severity)
    {
        Assert.False(evaluation.Approved);
        Assert.Equal("HumanRequired", evaluation.Status);
        var finding = Assert.Single(evaluation.Findings, item => item.RuleId == ruleId);
        Assert.Equal(severity, finding.Severity);
        Assert.True(evaluation.RequiresHumanApproval);
    }

    private static EngineHost CreateHost(IAEngineConsumerConfiguration configuration, IValidationRunner validation, int validationRemediationCycles = 5)
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

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("resilience-validation", ["resilience"], "low", "high", [], [], "local", "deterministic local route", true));

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
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m9-resilience-contract-gate");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local-fake");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }

    private sealed class RecoveringRunner(IValidationRunner first, IValidationRunner second) : IValidationRunner
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Calls == 1 ? first.RunAsync(cancellationToken) : second.RunAsync(cancellationToken);
        }

        public Task<IReadOnlyList<ValidationResult>> RunAsync(EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => RunAsync(cancellationToken);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("infrasentinel-resilience-");
        public string Path => directory.FullName;
        public void Dispose() => directory.Delete(true);
    }
}
