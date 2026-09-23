using InfraSentinel.Core;
using InfraSentinel.Core.Validation;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Roadmap;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class IAEngineMilestoneTests
{
    [Fact]
    public async Task SyntheticMilestoneRunsAllTasksAndPersistsIsolatedState()
    {
        var workspace = Directory.CreateTempSubdirectory("infrasentinel-milestone-");
        try
        {
            var configuration = new IAEngineConsumerConfiguration(workspace.FullName);
            var host = CreateHost(configuration, workspace.FullName, fixture: SafeFixture());

            var completed = await host.RunMilestoneAsync("sentinel-bootstrap-validation");
            var approved = await host.ApproveMilestoneAsync("sentinel-bootstrap-validation");

            Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, completed.Status);
            Assert.Equal(3, completed.CompletedTasks);
            Assert.Equal(3, completed.TotalTasks);
            Assert.Equal(MilestoneRuntimeStatus.Approved, approved.Status);
            Assert.True(File.Exists(Path.Combine(workspace.FullName, configuration.StateDirectory, "roadmap-state.json")));

            var runDirectories = Directory.GetDirectories(Path.Combine(workspace.FullName, configuration.RunsDirectory));
            Assert.Equal(3, runDirectories.Length);
            Assert.All(runDirectories, directory => Assert.True(File.Exists(Path.Combine(directory, "run.json"))));
            Assert.False(Directory.Exists(Path.Combine(workspace.FullName, ".ai-runs")));
        }
        finally
        {
            workspace.Delete(true);
        }
    }

    [Fact]
    public async Task SyntheticMilestonePreservesControlledHumanRequiredFailure()
    {
        var workspace = Directory.CreateTempSubdirectory("infrasentinel-milestone-");
        try
        {
            var configuration = new IAEngineConsumerConfiguration(workspace.FullName);
            var host = CreateHost(configuration, workspace.FullName, fixture: UnsafeFixture(), validationRemediationCycles: 0);

            var result = await host.RunMilestoneAsync("sentinel-bootstrap-validation");

            Assert.Equal(MilestoneRuntimeStatus.HumanRequired, result.Status);
            Assert.False(result.Succeeded);
            Assert.True(result.RequiresHumanApproval);
            Assert.Equal(0, result.CompletedTasks);
            Assert.True(File.Exists(Path.Combine(workspace.FullName, configuration.StateDirectory, "roadmap-state.json")));
            var validationArtifacts = Directory.EnumerateFiles(Path.Combine(workspace.FullName, configuration.RunsDirectory), "validation-*.json", SearchOption.AllDirectories);
            Assert.Contains(validationArtifacts, artifact => File.ReadAllText(artifact).Contains("explicit-insecure-configuration", StringComparison.Ordinal));
        }
        finally
        {
            workspace.Delete(true);
        }
    }

    private static EngineHost CreateHost(
        IAEngineConsumerConfiguration configuration,
        string workspace,
        bool reviewPasses = true,
        SyntheticInfrastructureFixture? fixture = null,
        int validationRemediationCycles = 5)
    {
        fixture ??= SafeFixture();
        return EngineHost.Create(new EngineHostContext
        {
            ProjectId = IAEngineConsumerConfiguration.ProjectId,
            WorkspaceRoot = workspace,
            Options = configuration.CreateOptions(reviewPasses ? 5 : 0, validationRemediationCycles),
            Composition = configuration.Composition,
            Components = new("local-router", "local-implementation", "local-review", "validation"),
            RegisterComponents = builder => builder
                .RegisterProvider<ITaskRouter>("local-router", () => new LocalRouter())
                .RegisterProvider<IImplementationAgent>("local-implementation", () => new LocalImplementation())
                .RegisterProvider<IReviewAgent>("local-review", () => new LocalReview(reviewPasses))
                .RegisterValidator<IValidationRunner>("local-validator", () => new InfrastructureValidationRunner(new DeterministicInfrastructureValidator(), fixture))
                .RegisterPolicy("local-policy", () => new LocalPolicy())
                .RegisterCapability<IValidationRunner>("validation", () => new InfrastructureValidationRunner(new DeterministicInfrastructureValidator(), fixture)),
            RunStore = new RunStore(workspace, configuration.RunsDirectory),
            Git = new LocalGit(workspace),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory
        });
    }

    private static SyntheticInfrastructureFixture SafeFixture() => new(
        [new("fixture-network", "network")],
        ["minimum-policy"],
        ["minimum-policy"]);

    private static SyntheticInfrastructureFixture UnsafeFixture() => new(
        [new("fixture-network", "network", ExplicitlyInsecure: true)],
        ["minimum-policy"],
        []);

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("synthetic", ["local"], "low", "low", [], [], "local", "local fake", true));

        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "local fake"));
    }

    private sealed class LocalImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "local fake implementation", []));

        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => Task.FromResult(new ImplementationResult(true, "local fake remediation", []));
    }

    private sealed class LocalReview(bool passes) : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(passes
                ? new ReviewResult(ReviewDecision.Pass, [], "local fake review")
                : new ReviewResult(ReviewDecision.Fail, [new ReviewFinding(FindingSeverity.High, "synthetic", "1", "controlled failure", "test", "human action")], "controlled failure"));
    }

    private sealed class LocalValidation : IValidationRunner
    {
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationResult>>([new("synthetic", true, new ProcessResult("local-validation", 0, "", "", TimeSpan.Zero))]);
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/synthetic-milestone-execution");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local-fake");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, "local fake branch"));
    }
}
