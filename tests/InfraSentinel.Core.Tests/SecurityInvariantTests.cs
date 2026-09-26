using System.Diagnostics;
using System.Text.Json;
using IAEngine.Core.Git;
using InfraSentinel.Core;
using InfraSentinel.Core.Integration;
using InfraSentinel.Core.Security;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Roadmap;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class SecurityInvariantTests
{
    [Fact]
    public void SafeFixtureIsApproved()
    {
        var result = new SecurityInvariantValidator().Evaluate(SafeFixture());

        Assert.Equal(SecurityInvariantStatus.Approved, result.Status);
        Assert.Empty(result.Findings);
        Assert.False(result.RequiresHumanApproval);
    }

    [Theory]
    [InlineData("public-exposure")]
    [InlineData("encryption")]
    [InlineData("least-privilege")]
    [InlineData("plaintext-secret")]
    [InlineData("audit-logging")]
    [InlineData("dependency-owner")]
    public void SecurityRulesProduceExpectedFindings(string scenario)
    {
        var fixture = scenario switch
        {
            "public-exposure" => SafeFixture() with { Resources = [SafeResource() with { Exposure = SecurityExposure.Public }] },
            "encryption" => SafeFixture() with { Resources = [SafeResource() with { EncryptionEnabled = false }] },
            "least-privilege" => SafeFixture() with { Identities = [new("service", "read documents", [new("admin", "*", "*", true, "fixture permission")])] },
            "plaintext-secret" => SafeFixture() with { Secrets = [new("api-key", "local-secret", true)] },
            "audit-logging" => SafeFixture() with { Resources = [SafeResource() with { AuditLoggingEnabled = false }] },
            "dependency-owner" => SafeFixture() with { Dependencies = [new("billing", "billing service", true, null, null)] },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = new SecurityInvariantValidator().Evaluate(fixture);

        Assert.Contains(result.Findings, finding => finding.Status == SecurityFindingStatus.Failed);
        Assert.True(result.RequiresHumanApproval);
        Assert.Contains(result.Findings, finding => finding.RuleId switch
        {
            "public-exposure" => false,
            _ => true
        });
    }

    [Fact]
    public void PlaintextSecretIsCriticalAndRequiresHumanApproval()
    {
        var result = new SecurityInvariantValidator().Evaluate(SafeFixture() with { Secrets = [new("secret", "do-not-use", true)] });
        var finding = Assert.Single(result.Findings, finding => finding.RuleId == "plaintext-secret-prohibited");

        Assert.Equal(SecurityFindingSeverity.Critical, finding.Severity);
        Assert.True(finding.RequiresHumanApproval);
        Assert.Equal(SecurityInvariantStatus.HumanRequired, result.Status);
    }

    [Fact]
    public void EvaluationIsDeterministic()
    {
        var validator = new SecurityInvariantValidator();
        var first = JsonSerializer.Serialize(validator.Evaluate(SafeFixture() with { Secrets = [new("secret", "x", true)] }));
        var second = JsonSerializer.Serialize(validator.Evaluate(SafeFixture() with { Secrets = [new("secret", "x", true)] }));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RunnerPersistsSecurityArtifact()
    {
        var workspace = Directory.CreateTempSubdirectory("infrasentinel-security-");
        try
        {
            var path = Path.Combine(workspace.FullName, ".ai-runs-infrasentinel", "security-invariant-gate.json");
            var result = await new SecurityInvariantValidationRunner(new SecurityInvariantValidator(), SafeFixture(), path).RunAsync();

            Assert.True(result.Single().Passed);
            Assert.True(File.Exists(path));
            var artifact = await File.ReadAllTextAsync(path);
            Assert.Contains("security-invariant-gate", artifact, StringComparison.Ordinal);
            Assert.Contains("NotRequested", artifact, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(workspace.FullName, ".ai-runs")));
            Assert.False(Directory.Exists(Path.Combine(workspace.FullName, ".ai-state")));
        }
        finally { workspace.Delete(true); }
    }

    [Fact]
    public async Task EngineHostRunsSafeFixtureAndCheckpointAfterApproval()
    {
        using var repository = GitRepository.Create();
        var configuration = new IAEngineConsumerConfiguration(repository.Root);
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var host = CreateHost(configuration, git, processes, new SecurityInvariantValidationRunner(new SecurityInvariantValidator(), SafeFixture(), configuration.SecurityInvariantArtifactPath));

        var completed = await host.RunMilestoneAsync("sentinel-security-invariant-gate");
        Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, completed.Status);
        Assert.Null(completed.Checkpoint);
        Assert.Null(repository.ReadCommitSubject());

        var approved = await host.ApproveMilestoneAsync("sentinel-security-invariant-gate");

        Assert.Equal(MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.True(approved.Checkpoint!.Result!.Succeeded);
        Assert.False(approved.Checkpoint.Result.PushPerformed);
        Assert.False(approved.Checkpoint.Result.MergePerformed);
        Assert.Equal("feat: add security invariant gate", repository.ReadCommitSubject());
    }

    [Fact]
    public async Task CriticalFixtureStopsAtHumanRequiredWithoutCommit()
    {
        using var repository = GitRepository.Create();
        var configuration = new IAEngineConsumerConfiguration(repository.Root);
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var critical = SafeFixture() with { Secrets = [new("secret", "plaintext", true)] };
        var host = CreateHost(configuration, git, processes, new SecurityInvariantValidationRunner(new SecurityInvariantValidator(), critical, configuration.SecurityInvariantArtifactPath), validationRemediationCycles: 0);

        var result = await host.RunMilestoneAsync("sentinel-security-invariant-gate");

        Assert.Equal(MilestoneRuntimeStatus.HumanRequired, result.Status);
        Assert.True(result.RequiresHumanApproval);
        Assert.Null(result.Checkpoint);
        Assert.Null(repository.ReadCommitSubject());
        Assert.True(File.Exists(configuration.SecurityInvariantArtifactPath));
    }

    [Fact]
    public async Task CorrectableFixturePassesBoundedRemediation()
    {
        using var repository = GitRepository.Create();
        var configuration = new IAEngineConsumerConfiguration(repository.Root);
        var processes = new ProcessRunner();
        var git = new GitService(processes, repository.Root, new GitOptions());
        var runner = new SequencedSecurityRunner(
            new SecurityInvariantValidationRunner(new SecurityInvariantValidator(), SafeFixture() with { Secrets = [new("secret", "plaintext", true)] }, configuration.SecurityInvariantArtifactPath),
            new SecurityInvariantValidationRunner(new SecurityInvariantValidator(), SafeFixture(), configuration.SecurityInvariantArtifactPath));
        var host = CreateHost(configuration, git, processes, runner, validationRemediationCycles: 1);

        var result = await host.RunMilestoneAsync("sentinel-security-invariant-gate");

        Assert.Equal(MilestoneRuntimeStatus.CompleteAwaitingApproval, result.Status);
        Assert.Null(result.Checkpoint);
        Assert.Null(repository.ReadCommitSubject());
    }

    private static EngineHost CreateHost(
        IAEngineConsumerConfiguration configuration,
        IGitService git,
        IProcessRunner processes,
        IValidationRunner validation,
        int validationRemediationCycles = 5)
    {
        var source = configuration.CreateCheckpointRequestSource(
            "feature/m12-security-invariant-gate",
            ["fixture.txt"],
            "feat: add security invariant gate");
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
            CheckpointRequestSource = source
        });
    }

    private static SecurityInvariantFixture SafeFixture()
        => new(
            "security-safe",
            "1",
            "security-execution",
            [SafeResource()],
            [new("reader", "read documents", [new("read", "read", "documents", false, "read-only fixture")])],
            [new("managed-secret", "reference-only", false)],
            [new("identity", "identity service", true, "security-owner", "pending state")],
            ["local synthetic evidence"]);

    private static SecurityResource SafeResource()
        => new("documents", SecurityResourceClassification.Sensitive, SecurityExposure.Private, true, true, true, "data-owner");

    private sealed class SequencedSecurityRunner(IValidationRunner first, IValidationRunner second) : IValidationRunner
    {
        private int calls;
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref calls) == 1 ? first.RunAsync(cancellationToken) : second.RunAsync(cancellationToken);
    }

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("synthetic", ["security"], "low", "low", [], [], "local", "local", true));
        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "local"));
    }

    private sealed class LocalImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "local implementation", []));
        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => Task.FromResult(new ImplementationResult(true, "bounded local remediation", []));
    }

    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class GitRepository : IDisposable
    {
        public string Root { get; }
        private GitRepository(string root) => Root = root;

        public static GitRepository Create()
        {
            var root = Directory.CreateTempSubdirectory("infrasentinel-security-").FullName;
            Run(root, "init", "-b", "feature/m12-security-invariant-gate");
            Run(root, "config", "user.email", "sentinel-tests@example.invalid");
            Run(root, "config", "user.name", "InfraSentinel Tests");
            File.WriteAllText(Path.Combine(root, ".gitignore"), ".ai-runs-infrasentinel/\n.ai-state-infrasentinel/\n");
            File.WriteAllText(Path.Combine(root, "fixture.txt"), "baseline\n");
            Run(root, "add", ".");
            Run(root, "commit", "-m", "chore: create security fixture");
            File.AppendAllText(Path.Combine(root, "fixture.txt"), "security gate\n");
            return new GitRepository(root);
        }

        public string? ReadCommitSubject()
        {
            var result = Run(root: Root, arguments: ["log", "-1", "--pretty=%s"], allowFailure: true);
            return result.ExitCode == 0 && !result.StandardOutput.Contains("create security fixture", StringComparison.Ordinal)
                ? result.StandardOutput.Trim()
                : null;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static ProcessResult Run(string root, params string[] arguments) => Run(root, arguments, false);

        private static ProcessResult Run(string root, IReadOnlyList<string> arguments, bool allowFailure)
        {
            var startInfo = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
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
