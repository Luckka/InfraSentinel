using InfraSentinel.Core;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class IAEngineLocalHostTests
{
    [Fact]
    public async Task InfraSentinelRunsSyntheticTaskThroughLocalEngineHost()
    {
        var workspace = Directory.CreateTempSubdirectory("infrasentinel-host-");
        try
        {
            var configuration = new IAEngineConsumerConfiguration(workspace.FullName);
            var host = EngineHost.Create(new EngineHostContext
            {
                ProjectId = IAEngineConsumerConfiguration.ProjectId,
                WorkspaceRoot = workspace.FullName,
                Options = configuration.CreateOptions(),
                Composition = configuration.Composition,
                Components = new("local-router", "local-implementation", "local-review", "validation"),
                RegisterComponents = builder => builder
                    .RegisterProvider<ITaskRouter>("local-router", () => new LocalRouter())
                    .RegisterProvider<IImplementationAgent>("local-implementation", () => new LocalImplementation())
                    .RegisterProvider<IReviewAgent>("local-review", () => new LocalReview())
                    .RegisterValidator<IValidationRunner>("local-validator", () => new LocalValidation())
                    .RegisterPolicy("local-policy", () => new LocalPolicy())
                    .RegisterCapability<IValidationRunner>("validation", () => new LocalValidation()),
                RunStore = new RunStore(workspace.FullName, configuration.RunsDirectory),
                Git = new LocalGit(workspace.FullName)
            });

            var result = await host.RunAsync(new DevelopmentTask(
                "SYNTHETIC-001",
                "Local host smoke task",
                "Prove that InfraSentinel can execute a safe synthetic task through IAEngine."));

            Assert.True(result.Succeeded);
            Assert.Equal(WorkflowState.Approved, result.State);
            Assert.Equal(IAEngineConsumerConfiguration.ProjectId, result.ProjectId);
            Assert.True(File.Exists(Path.Combine(workspace.FullName, configuration.RunsDirectory, result.RunId, "run.json")));
            Assert.False(Directory.Exists(Path.Combine(workspace.FullName, ".ai-runs")));
        }
        finally
        {
            workspace.Delete(true);
        }
    }

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

    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local fake review"));
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
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/local-iaengine-host-integration");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local-fake");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, "local fake branch"));
    }
}
