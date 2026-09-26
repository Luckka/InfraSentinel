using System.Diagnostics;
using IAEngine.Core.Git;
using InfraSentinel.Core;
using InfraSentinel.Core.Integration;
using InfraSentinel.Core.Validation;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Roadmap;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class CheckpointIntegrationTests
{
    [Fact]
    public async Task EngineHostRunsMilestoneAndCommitsOnlyAfterExplicitApproval()
    {
        using var repository = GitRepository.Create();
        var configuration = new IAEngineConsumerConfiguration(repository.Root);
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var host = CreateHost(configuration, git, processes);

        var completed = await host.RunMilestoneAsync("sentinel-engine-controlled-checkpoint");

        Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, completed.Status);
        Assert.Null(completed.Checkpoint);
        Assert.Null(repository.ReadCommitSubject());

        var approved = await host.ApproveMilestoneAsync("sentinel-engine-controlled-checkpoint");

        Assert.Equal(MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.NotNull(approved.Checkpoint);
        Assert.True(approved.Checkpoint!.Decision.Status == GitCheckpointDecisionStatus.Allowed, approved.Checkpoint.Decision.Reason);
        Assert.True(approved.Checkpoint.Result!.Succeeded);
        Assert.False(approved.Checkpoint.Result.PushPerformed);
        Assert.False(approved.Checkpoint.Result.MergePerformed);
        Assert.False(string.IsNullOrWhiteSpace(approved.Checkpoint.Result.CommitSha));
        Assert.Equal("test: validate engine-controlled sentinel checkpoint", repository.ReadCommitSubject());
        Assert.True(File.Exists(Path.Combine(repository.Root, configuration.RunsDirectory, "checkpoint", "sentinel-engine-controlled-checkpoint.json")));
        Assert.False(Directory.Exists(Path.Combine(repository.Root, ".ai-runs")));
        Assert.False(Directory.Exists(Path.Combine(repository.Root, ".ai-state")));
    }

    [Fact]
    public async Task HumanRequiredBlocksCommitAndWritesExplainableArtifact()
    {
        using var repository = GitRepository.Create();
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var coordinator = new InfraSentinelGitCheckpointCoordinator(git, processes, ".ai-runs-infrasentinel");
        var request = ValidRequest(repository.Root) with { State = GitCheckpointState.HumanRequired, HumanApprovalRequired = true, HumanApprovalProvided = false };

        var decision = await coordinator.EvaluateAsync(request);
        var result = await coordinator.CommitAsync(request);

        Assert.Equal(GitCheckpointDecisionStatus.HumanRequired, decision.Status);
        Assert.False(result.Succeeded);
        Assert.Null(result.CommitSha);
        Assert.False(result.PushPerformed);
        Assert.False(result.MergePerformed);
        Assert.Null(repository.ReadCommitSubject());
        Assert.True(File.Exists(Path.Combine(repository.Root, ".ai-runs-infrasentinel", "checkpoint", "CHECKPOINT-001.json")));
    }

    [Fact]
    public async Task CoordinatorBlocksUnrelatedFilesAndInvalidBranchWithoutCommit()
    {
        using var repository = GitRepository.Create();
        File.WriteAllText(Path.Combine(repository.Root, "unrelated.txt"), "unrelated");
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var coordinator = new InfraSentinelGitCheckpointCoordinator(git, processes, ".ai-runs-infrasentinel");
        var request = ValidRequest(repository.Root) with { CurrentBranch = "main", ExpectedFiles = ["fixture.txt"] };

        var decision = await coordinator.EvaluateAsync(request);

        Assert.Equal(GitCheckpointDecisionStatus.Blocked, decision.Status);
        Assert.Contains(decision.Evidence, evidence => evidence is "branch-not-authorized" or "unrelated-changes");
        Assert.Null(repository.ReadCommitSubject());
    }

    [Fact]
    public async Task ValidationReviewAndSemanticMessageFailuresBlockCommit()
    {
        using var repository = GitRepository.Create();
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var coordinator = new InfraSentinelGitCheckpointCoordinator(git, processes, ".ai-runs-infrasentinel");

        var validationFailure = await coordinator.EvaluateAsync(ValidRequest(repository.Root) with { ValidationPassed = false });
        var reviewFailure = await coordinator.EvaluateAsync(ValidRequest(repository.Root) with { ReviewPassed = false });
        var messageFailure = await coordinator.EvaluateAsync(ValidRequest(repository.Root) with { ProposedCommitMessage = "update files" });

        Assert.Contains(validationFailure.Evidence, evidence => evidence == "validation-failed");
        Assert.Contains(reviewFailure.Evidence, evidence => evidence == "review-failed");
        Assert.Contains(messageFailure.Evidence, evidence => evidence == "invalid-semantic-message");
        Assert.Null(repository.ReadCommitSubject());
    }

    [Fact]
    public async Task EngineMilestoneRecoversThroughBoundedValidationRemediation()
    {
        using var repository = GitRepository.Create();
        var configuration = new IAEngineConsumerConfiguration(repository.Root);
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var host = CreateHost(configuration, git, processes, new RecoveringValidation(), validationRemediationCycles: 1);

        var result = await host.RunMilestoneAsync("sentinel-engine-controlled-checkpoint");

        Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, result.Status);
        Assert.Null(result.Checkpoint);
        Assert.Null(repository.ReadCommitSubject());
    }

    [Fact]
    public void SentinelConsumesCoreAndNotTheOnlineOsAdapter()
    {
        var references = typeof(InfraSentinelGitCheckpointCoordinator).Assembly.GetReferencedAssemblies();

        Assert.Contains(references, reference => reference.Name == "IAEngine.Core");
        Assert.DoesNotContain(references, reference => reference.Name == "IAEngine.OnlineOSAdapter");
    }

    private static EngineHost CreateHost(
        IAEngineConsumerConfiguration configuration,
        IGitService git,
        IProcessRunner processes,
        IValidationRunner? validationOverride = null,
        int validationRemediationCycles = 5)
    {
        var safeFixture = new SyntheticInfrastructureFixture([new("checkpoint-resource", "document")], ["minimum-policy"], ["minimum-policy"]);
        var validation = validationOverride ?? new InfrastructureValidationRunner(new DeterministicInfrastructureValidator(), safeFixture);
        var requestSource = configuration.CreateCheckpointRequestSource(
            "feature/sentinel-checkpoint",
            ["fixture.txt"],
            "test: validate engine-controlled sentinel checkpoint");
        return EngineHost.Create(new EngineHostContext
        {
            ProjectId = IAEngineConsumerConfiguration.ProjectId,
            WorkspaceRoot = configuration.WorkspaceRoot,
            Options = configuration.CreateOptions(validationRemediationCycles: validationRemediationCycles),
            Composition = configuration.Composition,
            Components = new("local-router", "local-implementation", "local-review", "validation"),
            RegisterComponents = builder => builder
                .RegisterProvider<ITaskRouter>("local-router", () => new LocalRouter())
                .RegisterProvider<IImplementationAgent>("local-implementation", () => new LocalImplementation())
                .RegisterProvider<IReviewAgent>("local-review", () => new LocalReview())
                .RegisterValidator<IValidationRunner>("local-validator", () => validation)
                .RegisterCapability<IValidationRunner>("validation", () => validation)
                .RegisterPolicy("local-policy", () => new LocalPolicy()),
            RunStore = new RunStore(configuration.WorkspaceRoot, configuration.RunsDirectory),
            Git = git,
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory,
            CheckpointCoordinator = configuration.CreateCheckpointCoordinator(git, processes),
            CheckpointRequestSource = requestSource
        });
    }

    private static GitCheckpointRequest ValidRequest(string root)
        => new(
            IAEngineConsumerConfiguration.ProjectId,
            root,
            GitCheckpointScope.Task,
            null,
            "CHECKPOINT-001",
            "feature/sentinel-checkpoint",
            "test: validate engine-controlled sentinel checkpoint",
            GitCheckpointState.Approved,
            true,
            true,
            true,
            true,
            false,
            false,
            false,
            true,
            true,
            ["fixture.txt"],
            ["fixture.txt"],
            true,
            true,
            false,
            false);

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("synthetic", ["local"], "low", "low", [], [], "local", "local route", true));

        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "local"));
    }

    private sealed class LocalImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "local implementation", []));

        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => Task.FromResult(new ImplementationResult(true, "local remediation", []));
    }

    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class RecoveringValidation : IValidationRunner
    {
        private int attempts;

        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passed = Interlocked.Increment(ref attempts) > 1;
            return Task.FromResult<IReadOnlyList<ValidationResult>>([
                new("CheckpointRecovery", true, new ProcessResult("local-recovery-validation", passed ? 0 : 1, "", "", TimeSpan.Zero), passed ? ValidationStatus.Pass : ValidationStatus.Fail)
            ]);
        }
    }

    private sealed class GitRepository : IDisposable
    {
        public string Root { get; }

        private GitRepository(string root) => Root = root;

        public static GitRepository Create()
        {
            var root = Directory.CreateTempSubdirectory("infrasentinel-checkpoint-").FullName;
            Run(root, "init", "-b", "feature/sentinel-checkpoint");
            Run(root, "config", "user.email", "sentinel-tests@example.invalid");
            Run(root, "config", "user.name", "InfraSentinel Tests");
            File.WriteAllText(Path.Combine(root, ".gitignore"), ".ai-runs-infrasentinel/\n.ai-state-infrasentinel/\n");
            File.WriteAllText(Path.Combine(root, "fixture.txt"), "baseline\n");
            Run(root, "add", ".");
            Run(root, "commit", "-m", "chore: create checkpoint fixture");
            File.AppendAllText(Path.Combine(root, "fixture.txt"), "validated\n");
            return new GitRepository(root);
        }

        public string? ReadCommitSubject()
        {
            var result = Run(Root, new[] { "log", "-1", "--pretty=%s" }, true);
            return result.ExitCode == 0 && !result.StandardOutput.Contains("create checkpoint fixture", StringComparison.Ordinal)
                ? result.StandardOutput.Trim()
                : null;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static ProcessResult Run(string root, params string[] arguments)
            => Run(root, arguments, false);

        private static ProcessResult Run(string root, IReadOnlyList<string> arguments, bool allowFailure)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)!;
            process.WaitForExit();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!allowFailure && process.ExitCode != 0) throw new InvalidOperationException(error);
            return new ProcessResult("git", process.ExitCode, output, error, TimeSpan.Zero);
        }
    }
}
