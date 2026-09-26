using IAEngine.Core.Git;
using InfraSentinel.Core;
using InfraSentinel.Core.Adapters;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class ProjectAdapterBoundaryTests
{
    [Fact]
    public async Task DotNetAdapterProducesNeutralModelWithoutExecutingProject()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "sample.sln"), "Microsoft Visual Studio Solution File, Format Version 12.00");
        File.WriteAllText(Path.Combine(workspace.Root, "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Example\" Version=\"1.2.3\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(workspace.Root, "appsettings.example.json"), "{\"Logging\":{\"LogLevel\":{\"Default\":\"Information\"}},\"Encryption\":{\"Enabled\":true}}");

        var result = await new DotNetProjectAdapter().AnalyzeAsync(Request(workspace.Root));

        Assert.Equal(ProjectAdapterStatus.Analyzed, result.Status);
        Assert.Equal("dotnet", result.ProjectType);
        Assert.Equal("net10.0", result.Model!.ProjectVersion);
        Assert.Contains(result.Model.Dependencies, dependency => dependency.Name == "Example");
        Assert.Contains("/sample.csproj", result.Model.AnalyzedFiles);
        Assert.DoesNotContain(result.Evidence, item => item.Contains("dotnet build", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NodeAdapterProducesDeterministicNeutralModel()
    {
        using var first = new TemporaryWorkspace();
        using var second = new TemporaryWorkspace();
        const string package = "{\"name\":\"synthetic-node\",\"version\":\"2.0.0\",\"dependencies\":{\"express\":\"4.0.0\"},\"devDependencies\":{\"vitest\":\"1.0.0\"}}";
        File.WriteAllText(Path.Combine(first.Root, "package.json"), package);
        File.WriteAllText(Path.Combine(second.Root, "package.json"), package);

        var adapter = new NodeProjectAdapter();
        var left = await adapter.AnalyzeAsync(Request(first.Root));
        var right = await adapter.AnalyzeAsync(Request(second.Root));

        Assert.Equal(ProjectAdapterStatus.Analyzed, left.Status);
        Assert.Equal(left.Model!.ProjectType, right.Model!.ProjectType);
        Assert.Equal(left.Model.ProjectVersion, right.Model.ProjectVersion);
        Assert.Equal(left.Model.Dependencies, right.Model.Dependencies);
        Assert.Equal(left.Model.Components, right.Model.Components);
    }

    [Fact]
    public async Task UnsupportedWorkspaceFailsClosedWithoutAdapter()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "README.md"), "unsupported");
        var selector = new ProjectAdapterSelector([new DotNetProjectAdapter(), new NodeProjectAdapter()]);
        var selected = await selector.SelectAsync(Request(workspace.Root));

        Assert.Null(selected);
    }

    [Fact]
    public async Task UnsupportedWorkspaceProducesBlockedEvidenceWithoutCheckpoint()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "README.md"), "unsupported");
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new ProjectAdapterEvidence("unsupported-execution", "project-adapter-boundary-validation", IAEngineConsumerConfiguration.ProjectId, "feature/m14-project-adapter-boundary");
        var writer = configuration.CreateProjectAdapterArtifactWriter();
        var results = await new ProjectAdapterValidationRunner(configuration.CreateProjectAdapterSelector(), Request(workspace.Root), evidence, writer).RunAsync();

        Assert.False(results.Single().Passed);
        Assert.Equal("Blocked", evidence.BuildArtifact().Status);
        Assert.Null(evidence.BuildArtifact().CommitSha);
        Assert.False(evidence.BuildArtifact().PushPerformed);
        Assert.False(evidence.BuildArtifact().MergePerformed);
        Assert.True(File.Exists(configuration.ProjectAdapterArtifactPath));
    }

    [Fact]
    public async Task InvalidWorkspaceAndSecurityLimitsAreRejected()
    {
        Assert.False(ProjectAdapterPathPolicy.IsSafeRelativePath("../outside.json"));
        Assert.False(ProjectAdapterPathPolicy.IsSafeRelativePath("/absolute.json"));
        Assert.True(ProjectAdapterPathPolicy.IsSafeRelativePath("src/project.json"));

        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "package.json"), new string('x', 200));
        var result = await new NodeProjectAdapter().AnalyzeAsync(Request(workspace.Root, new ProjectAdapterOptions(MaxFileBytes: 32)));

        Assert.Equal(ProjectAdapterStatus.Failed, result.Status);
        Assert.Contains("package.json", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecretDetectionDoesNotPersistSecretContent()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "package.json"), "{\"name\":\"secret-fixture\",\"client_secret\":\"DO_NOT_PERSIST\"}");
        var result = await new NodeProjectAdapter().AnalyzeAsync(Request(workspace.Root));

        Assert.Contains(result.Findings, finding => finding.RuleId == "ADAPTER-SECRET-CONTENT");
        Assert.DoesNotContain(result.Evidence, item => item.Contains("DO_NOT_PERSIST", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Limitations, item => item.Contains("DO_NOT_PERSIST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForbiddenDirectoriesAreIgnored()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "node_modules", "ignored"));
        File.WriteAllText(Path.Combine(workspace.Root, "package.json"), "{\"name\":\"safe\"}");
        File.WriteAllText(Path.Combine(workspace.Root, "node_modules", "ignored", "package.json"), "{\"name\":\"should-not-read\"}");

        var result = await new NodeProjectAdapter().AnalyzeAsync(Request(workspace.Root));

        Assert.Equal(ProjectAdapterStatus.Analyzed, result.Status);
        Assert.DoesNotContain(result.Model!.Components, component => component.Name == "should-not-read");
        Assert.Contains(result.Model.IgnoredFiles, file => file.Contains("node_modules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdapterValidationRunsThroughEngineHostAndWritesCheckpointArtifact()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "package.json"), "{\"name\":\"engine-fixture\",\"version\":\"1.0.0\"}");
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new ProjectAdapterEvidence("adapter-execution", "project-adapter-boundary-validation", IAEngineConsumerConfiguration.ProjectId, "feature/m14-project-adapter-boundary");
        var writer = configuration.CreateProjectAdapterArtifactWriter();
        var validation = new ProjectAdapterValidationRunner(configuration.CreateProjectAdapterSelector(), Request(workspace.Root), evidence, writer);
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
                .RegisterProvider<IReviewAgent>("local-review", () => new LocalReview())
                .RegisterValidator<IValidationRunner>("local-validator", () => validation)
                .RegisterCapability<IValidationRunner>("validation", () => validation)
                .RegisterPolicy("local-policy", () => new LocalPolicy()),
            RunStore = new RunStore(workspace.Root, configuration.RunsDirectory),
            Git = new LocalGit(workspace.Root),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory,
            CheckpointCoordinator = new ProjectAdapterCheckpointCoordinator(coordinator, evidence, writer),
            CheckpointRequestSource = new ProjectAdapterCheckpointRequestSource(
                configuration.CreateCheckpointRequestSource("feature/m14-project-adapter-boundary", ["package.json"], "test: validate project adapter boundary"), evidence)
        });

        var awaiting = await host.RunMilestoneAsync("project-adapter-boundary-validation");
        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.CompleteAwaitingApproval, awaiting.Status);
        Assert.Empty(coordinator.Commits);

        var approved = await host.ApproveMilestoneAsync("project-adapter-boundary-validation");
        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.Single(coordinator.Commits);
        Assert.True(File.Exists(configuration.ProjectAdapterArtifactPath));
        var artifact = await File.ReadAllTextAsync(configuration.ProjectAdapterArtifactPath);
        Assert.Contains("project-adapter-boundary", artifact, StringComparison.Ordinal);
        Assert.Contains("m14-sha", artifact, StringComparison.Ordinal);
        Assert.Contains("\"pushPerformed\": false", artifact, StringComparison.Ordinal);
        Assert.Contains("\"mergePerformed\": false", artifact, StringComparison.Ordinal);
    }

    private static ProjectAdapterRequest Request(string root, ProjectAdapterOptions? options = null)
        => new("fixture-project", root, options ?? new());

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

    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m14-project-adapter-boundary");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }

    private sealed class RecordingCoordinator : IGitCheckpointCoordinator
    {
        public List<GitCheckpointRequest> Commits { get; } = [];
        public Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(GitCheckpointPolicy.Evaluate(request));
        public Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
        {
            Commits.Add(request);
            return Task.FromResult(new GitCheckpointResult(true, "m14-sha", request.ProposedCommitMessage, request.CurrentBranch, request.ExpectedFiles, DateTimeOffset.UtcNow, null, true, false, false));
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("infrasentinel-m14-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
