using IAEngine.Core.Git;
using InfraSentinel.Core;
using InfraSentinel.Core.Cloud;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class CloudProviderSafetyTests
{
    [Fact]
    public async Task ProviderIsDisabledByDefaultAndDryRunIsAllowed()
    {
        var disabled = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request("DescribeBuckets", providerEnabled: false, dryRun: false));
        var dryRun = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request("DescribeBuckets"));

        Assert.Equal(CloudProviderSafetyStatus.Disabled, disabled.Decision.Status);
        Assert.Equal(CloudProviderSafetyStatus.Allowed, dryRun.Decision.Status);
        Assert.False(dryRun.Decision.RealCallAttempted);
    }

    [Theory]
    [InlineData("DescribeBuckets")]
    [InlineData("ListResources")]
    public async Task DescribeAndListAreAllowedOffline(string operation)
    {
        var result = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request(operation));
        Assert.True(result.Decision.Allowed);
        Assert.False(result.Decision.MutationAttempted);
    }

    [Fact]
    public async Task GetRequiresExplicitAuthorization()
    {
        var denied = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request("GetResource"));
        var allowed = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request("GetResource", allowGet: true));
        Assert.Equal(CloudProviderSafetyStatus.Blocked, denied.Decision.Status);
        Assert.Equal(CloudProviderSafetyStatus.Allowed, allowed.Decision.Status);
    }

    [Theory]
    [InlineData("CreateResource")]
    [InlineData("PutObject")]
    [InlineData("DeleteResource")]
    [InlineData("ModifyNetwork")]
    [InlineData("UnknownOperation")]
    public async Task MutatingAndUnknownOperationsAreBlocked(string operation)
    {
        var result = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request(operation));
        Assert.Equal(CloudProviderSafetyStatus.Blocked, result.Decision.Status);
        Assert.False(result.Decision.RealCallAttempted);
    }

    [Fact]
    public async Task AccountRegionAndCredentialMaterialAreFailClosed()
    {
        var account = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request("DescribeBuckets") with { Scope = new("other", ["allowed"], "us-east-1", ["us-east-1"]) });
        var region = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request("DescribeBuckets") with { Scope = new("allowed", ["allowed"], "eu-west-1", ["us-east-1"]) });
        var credential = await new OfflineAwsReadOnlyProvider().ObserveAsync(Request("DescribeBuckets") with { CredentialMaterial = "secret-token" });

        Assert.Contains("account-out-of-scope", account.Decision.Evidence);
        Assert.Contains("region-out-of-scope", region.Decision.Evidence);
        Assert.False(credential.Decision.CredentialSafety.Safe);
        Assert.DoesNotContain("secret-token", System.Text.Json.JsonSerializer.Serialize(credential));
    }

    [Fact]
    public async Task EnabledNonDryRunRequiresHumanApprovalWithoutCallingCloud()
    {
        var result = await new OfflineAwsReadOnlyProvider(providerEnabled: true).ObserveAsync(Request("DescribeBuckets", providerEnabled: true, dryRun: false));
        Assert.Equal(CloudProviderSafetyStatus.HumanRequired, result.Decision.Status);
        Assert.True(result.Decision.RequiresHumanApproval);
        Assert.False(result.Decision.RealCallAttempted);
    }

    [Fact]
    public async Task CancellationAndTimeoutAreExplicit()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new OfflineAwsReadOnlyProvider().ObserveAsync(Request("DescribeBuckets"), cancelled.Token));
        var timedOut = await new OfflineAwsReadOnlyProvider(simulateTimeout: true).ObserveAsync(Request("DescribeBuckets") with { Timeout = TimeSpan.FromMilliseconds(1) });
        Assert.Equal(CloudProviderSafetyStatus.TimedOut, timedOut.Decision.Status);
    }

    [Fact]
    public async Task RunnerPersistsSafeArtifactWithoutSecrets()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var runner = new CloudProviderSafetyValidationRunner(new OfflineAwsReadOnlyProvider(), Request("DescribeBuckets"), configuration.CloudProviderSafetyArtifactPath);
        var results = await runner.RunAsync();
        var artifact = await File.ReadAllTextAsync(configuration.CloudProviderSafetyArtifactPath);

        Assert.True(results.Single().Passed);
        Assert.Contains("aws-read-only-offline", artifact, StringComparison.Ordinal);
        Assert.Contains("\"realCallAttempted\": false", artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", artifact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunnerRetriesTimeoutOnlyWithinConfiguredLimit()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var provider = new SequenceBoundary([
            new(new(CloudProviderSafetyStatus.TimedOut, CloudProviderOperationCategory.Describe, "temporary timeout", ["timeout"], new(true, "safe"), false, false, true), 1, ["timeout"]),
            new(new(CloudProviderSafetyStatus.Allowed, CloudProviderOperationCategory.Describe, "offline", ["offline"], new(true, "safe"), false, false, false), 1, [])]);
        var runner = new CloudProviderSafetyValidationRunner(provider, Request("DescribeBuckets") with { MaxAttempts = 2 }, configuration.CloudProviderSafetyArtifactPath);

        var result = await runner.RunAsync();

        Assert.True(result.Single().Passed);
        Assert.Equal(2, provider.Calls);
        Assert.Contains("\"attempts\": 2", await File.ReadAllTextAsync(configuration.CloudProviderSafetyArtifactPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EngineHostExecutesCloudBoundaryMilestoneAndLocalCheckpoint()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var runner = new CloudProviderSafetyValidationRunner(new OfflineAwsReadOnlyProvider(), Request("DescribeBuckets"), configuration.CloudProviderSafetyArtifactPath);
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
                .RegisterValidator<IValidationRunner>("local-validator", () => runner)
                .RegisterCapability<IValidationRunner>("validation", () => runner)
                .RegisterPolicy("local-policy", () => new LocalPolicy()),
            RunStore = new RunStore(workspace.Root, configuration.RunsDirectory),
            Git = new LocalGit(workspace.Root),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory,
            CheckpointCoordinator = coordinator,
            CheckpointRequestSource = new InfraSentinelCheckpointRequestSource(workspace.Root, "feature/m17-cloud-provider-boundary", ["provider-safety.json"], "feat: validate cloud provider safety boundary")
        });

        var awaiting = await host.RunMilestoneAsync("cloud-provider-safety-boundary");
        var approved = await host.ApproveMilestoneAsync("cloud-provider-safety-boundary");

        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.CompleteAwaitingApproval, awaiting.Status);
        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.Single(coordinator.Commits);
        Assert.False(coordinator.Results[0].PushPerformed);
        Assert.False(coordinator.Results[0].MergePerformed);
    }

    private static CloudProviderObservationRequest Request(string operation, bool providerEnabled = false, bool dryRun = true, bool allowGet = false)
        => new("infra-sentinel", "aws", operation, new("allowed", ["allowed"], "us-east-1", ["us-east-1"]), true, providerEnabled, dryRun, allowGet, null, null, TimeSpan.FromMilliseconds(10), 2);

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
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default) => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local"));
    }
    private sealed record LocalPolicy(string Name = "local") : IProjectPolicyComponent;
    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m17-cloud-provider-boundary");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("m17-sha");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }
    private sealed class RecordingCoordinator : IGitCheckpointCoordinator
    {
        public List<GitCheckpointRequest> Commits { get; } = [];
        public List<GitCheckpointResult> Results { get; } = [];
        public Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default) => Task.FromResult(GitCheckpointPolicy.Evaluate(request));
        public Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
        {
            Commits.Add(request);
            var result = new GitCheckpointResult(true, "m17-sha", request.ProposedCommitMessage, request.CurrentBranch, request.ExpectedFiles, DateTimeOffset.UnixEpoch, null, true, false, false);
            Results.Add(result);
            return Task.FromResult(result);
        }
    }
    private sealed class SequenceBoundary(IReadOnlyList<CloudProviderObservationResult> values) : ICloudProviderSafetyBoundary
    {
        private int index;
        public int Calls { get; private set; }
        public Task<CloudProviderObservationResult> ObserveAsync(CloudProviderObservationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(values[Math.Min(index++, values.Count - 1)]);
        }
    }
    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("infrasentinel-m17-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
