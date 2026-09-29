using System.Text.Json;
using InfraSentinel.Core;
using InfraSentinel.Core.Cloud;
using InfraSentinel.Core.IaC;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class AwsReadOnlyObservationTests
{
    [Fact]
    public async Task LiveAwsIsDisabledByDefaultAndProviderFailsClosed()
    {
        var configuration = Configuration(live: false, dryRun: true);
        var client = new FakeClient();
        var provider = Provider(configuration, client);

        var result = await provider.GetSnapshotAsync(Request());

        Assert.Equal(CloudCollectionStatus.Failed, result.Status);
        Assert.Contains("live-aws-disabled", result.Limitations);
        Assert.Equal(0, client.ObserveCalls);
    }

    [Fact]
    public async Task WrongProfileAndMissingProfileAreBlockedBeforeClientCall()
    {
        var wrong = await Provider(Configuration(profile: "default", live: true, dryRun: false), new FakeClient()).GetSnapshotAsync(Request());
        var missing = await Provider(Configuration(profile: "", live: true, dryRun: false), new FakeClient()).GetSnapshotAsync(Request());

        Assert.Contains("profile-out-of-scope", wrong.Limitations);
        Assert.Contains("profile-out-of-scope", missing.Limitations);
    }

    [Fact]
    public async Task AccountAndRegionScopeAreFailClosed()
    {
        var accountClient = new FakeClient { Identity = new("999999999999", "user", "arn:aws:iam::999999999999:user/infra-sentinel") };
        var account = await Provider(Configuration(live: true, dryRun: false), accountClient).GetSnapshotAsync(Request());
        var region = await Provider(Configuration(live: true, dryRun: false), new FakeClient()).GetSnapshotAsync(Request() with { LogicalRegion = "sa-east-1" });

        Assert.Contains("account-out-of-scope", account.Limitations);
        Assert.Contains("region-out-of-scope", region.Limitations);
    }

    [Theory]
    [InlineData("EC2.DescribeInstances", true)]
    [InlineData("ECS.ListClusters", true)]
    [InlineData("EC2.ModifySecurityGroup", false)]
    [InlineData("SecretsManager.GetSecretValue", false)]
    [InlineData("", false)]
    public void ReadOnlyPolicyIsExactAndFailClosed(string operation, bool allowed)
        => Assert.Equal(allowed, new AwsReadOnlyPolicy().IsOperationAllowed(operation));

    [Fact]
    public async Task SensitiveResponseIsRejectedWithoutPersistingValue()
    {
        var client = new FakeClient
        {
            Result = new([new("bucket", "s3-bucket", "global", null, true, null, null, null, new Dictionary<string, string> { ["secret"] = "do-not-persist" }, [])], [], [], [], 0, false)
        };
        var result = await Provider(Configuration(live: true, dryRun: false), client).GetSnapshotAsync(Request());

        Assert.Contains("sanitized-response-rejected", result.Limitations);
        Assert.DoesNotContain("do-not-persist", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task EmptySnapshotIsDeterministicAndNormalized()
    {
        var configuration = Configuration(live: true, dryRun: false);
        var first = await Provider(configuration, new FakeClient()).GetSnapshotAsync(Request());
        var second = await Provider(configuration, new FakeClient()).GetSnapshotAsync(Request());

        Assert.Equal(CloudCollectionStatus.Collected, first.Status);
        Assert.NotNull(first.Snapshot);
        Assert.Empty(first.Snapshot!.Resources);
        Assert.Equal(JsonSerializer.Serialize(first.Snapshot), JsonSerializer.Serialize(second.Snapshot));
        Assert.StartsWith("aws-account-", first.Snapshot.AccountAlias, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessDeniedIsExplainableAndDoesNotTryAnotherProfile()
    {
        var client = new FakeClient { Failure = new AccessDeniedException() };
        var result = await Provider(Configuration(live: true, dryRun: false), client).GetSnapshotAsync(Request());

        Assert.Contains("aws-access-denied", result.Limitations);
        Assert.Equal(AwsReadOnlyConfiguration.RequiredProfile, client.LastProfile);
    }

    [Fact]
    public async Task TimeoutAndCancellationAreExplicit()
    {
        var timeoutClient = new FakeClient { Delay = TimeSpan.FromSeconds(1) };
        var timedOut = await Provider(Configuration(live: true, dryRun: false, timeout: TimeSpan.FromMilliseconds(10)), timeoutClient).GetSnapshotAsync(Request() with { Timeout = TimeSpan.FromMilliseconds(10) });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(CloudCollectionStatus.TimedOut, timedOut.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(Configuration(live: true, dryRun: false), new FakeClient { Delay = TimeSpan.FromSeconds(1) }).GetSnapshotAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task RetryLimitIsPassedBoundedToClient()
    {
        var client = new FakeClient();
        await Provider(Configuration(live: true, dryRun: false, maxAttempts: 99), client).GetSnapshotAsync(Request());

        Assert.Equal(3, client.LastMaxAttempts);
    }

    [Fact]
    public void EnvironmentConfigurationRequiresExplicitLiveOptIn()
    {
        var configuration = AwsReadOnlyConfiguration.FromEnvironment(new Dictionary<string, string?>
        {
            ["INFRA_SENTINEL_AWS_PROFILE"] = AwsReadOnlyConfiguration.RequiredProfile,
            ["INFRA_SENTINEL_AWS_ACCOUNT_ID"] = "123456789012"
        });

        Assert.False(configuration.LiveAwsEnabled);
        Assert.True(configuration.DryRun);
        Assert.Equal(["us-east-1"], configuration.AuthorizedRegions);
    }

    [Fact]
    public void ConfiguredFactoryUsesOfflineProviderWithoutExplicitLiveOptIn()
    {
        var configuration = Configuration(live: false, dryRun: true);
        var provider = new IAEngineConsumerConfiguration(Directory.GetCurrentDirectory()).CreateConfiguredCloudObservationProvider(configuration);

        Assert.Equal("synthetic-cloud", provider.ProviderId);
        Assert.Equal(CloudObservationSourceType.Synthetic, provider.SourceType);
    }

    [Fact]
    public async Task M18MilestoneUsesExistingEngineHostRoadmapBoundary()
    {
        var milestone = await new InfraSentinelMilestoneSource().LoadMilestoneAsync("real-aws-read-only-observation");

        Assert.Equal("real-aws-read-only-observation", milestone.Id);
        Assert.Equal(["AWS-001", "AWS-002", "AWS-003", "AWS-004", "AWS-005", "AWS-006", "AWS-007"], milestone.Tasks.Select(task => task.Id));
    }

    [Fact]
    public void IaCComparisonDoesNotTreatMissingResourcesAsSafe()
    {
        var desired = new NeutralIaCModel("infra-sentinel", "terraform", "1", [new("bucket", "storage", "resource", [], false, [], true, true, true, true, false, "owner", "lab", ["read"], "/main.tf", 1, ["main.tf:1"], [])], [], [], [], [], ["/main.tf"], [], []);
        var observed = new AwsReadOnlySnapshotNormalizer().Normalize(new("123456789012", "user", "arn:aws:iam::123456789012:user/infra-sentinel"), Configuration(live: true, dryRun: false), new([], [], [], [], 0, false));

        var evaluation = new IacCloudSnapshotComparator().Compare("m18", desired, observed);

        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "CLOUD-RESOURCE-MISSING");
        Assert.NotEqual(CloudObservationStatus.Approved, evaluation.Status);
    }

    [Fact]
    public async Task EvidenceWriterPersistsOnlyM18MetadataAndSanitizedSnapshot()
    {
        using var workspace = new TemporaryWorkspace();
        var metadata = new CloudObservationMetadata("aws-account-hash", ["us-east-1"], ["Ec2"], [new("EC2.DescribeInstances", "us-east-1", "success", 0)], [], 0, false);
        var result = new CloudObservationResult(CloudCollectionStatus.Collected, new("snapshot", "m18-1", "aws-read-only", "aws-account-hash", "us-east-1", "unknown", DateTimeOffset.UnixEpoch, CloudObservationSourceType.External, [], [], [], [], [], [], [], [], [], CloudCollectionStatus.Collected), [], "collected", false, metadata);
        var evidence = new CloudObservationEvidence("execution", "infra-sentinel", "real-aws-read-only-observation", "feature/m18-real-aws-read-only-observation");
        evidence.RecordObservation(result, new NeutralIaCModel("infra-sentinel", "terraform", "1", [], [], [], [], [], [], [], []));
        var writer = new CloudObservationArtifactWriter(Path.Combine(workspace.Root, "artifact.json"));

        await writer.WriteAsync(evidence);
        var text = await File.ReadAllTextAsync(Path.Combine(workspace.Root, "artifact.json"));

        Assert.Contains("aws-account-hash", text, StringComparison.Ordinal);
        Assert.Contains("EC2.DescribeInstances", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessKey", text, StringComparison.OrdinalIgnoreCase);
    }

    private static AwsReadOnlyObservationProvider Provider(AwsReadOnlyConfiguration configuration, FakeClient client)
        => new(configuration, client, new AwsIdentityVerifier(client), new AwsReadOnlyPolicy(), new AwsReadOnlySnapshotNormalizer());

    private static AwsReadOnlyConfiguration Configuration(string profile = AwsReadOnlyConfiguration.RequiredProfile, bool live = true, bool dryRun = false, TimeSpan? timeout = null, int maxAttempts = 2)
        => new(profile, "123456789012", ["us-east-1"], timeout ?? TimeSpan.FromSeconds(1), maxAttempts, dryRun, live, new HashSet<AwsObservedService>(Enum.GetValues<AwsObservedService>()));

    private static CloudObservationRequest Request()
        => new("infra-sentinel", "lab", "us-east-1", ["us-east-1"], [], TimeSpan.FromSeconds(1), 100, 2);

    private sealed class FakeClient : IAwsReadOnlyClient
    {
        public AwsIdentityObservation Identity { get; init; } = new("123456789012", "AIDA", "arn:aws:iam::123456789012:user/infra-sentinel");
        public AwsReadOnlyClientResult Result { get; init; } = new([], [], [], [], 0, false);
        public Exception? Failure { get; init; }
        public TimeSpan Delay { get; init; }
        public string? LastProfile { get; private set; }
        public int LastMaxAttempts { get; private set; }
        public int ObserveCalls { get; private set; }

        public Task<AwsIdentityObservation> GetIdentityAsync(string profileName, CancellationToken cancellationToken = default)
        {
            LastProfile = profileName;
            return Task.FromResult(Identity);
        }

        public async Task<AwsReadOnlyClientResult> ObserveAsync(string profileName, IReadOnlyList<string> regions, IReadOnlySet<AwsObservedService> services, TimeSpan timeout, int maxAttempts, CancellationToken cancellationToken = default)
        {
            LastProfile = profileName;
            LastMaxAttempts = maxAttempts;
            ObserveCalls++;
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            if (Failure is not null) throw Failure;
            return Result;
        }
    }

    private sealed class AccessDeniedException : Exception;

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("infrasentinel-m18-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
