using InfraSentinel.Core;
using InfraSentinel.Core.Cloud;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class AwsReadOnlyLiveTests
{
    [LiveAwsFact]
    public async Task OptInLiveObservationUsesOnlyTheAuthorizedReadOnlyAdapter()
    {
        var configuration = AwsReadOnlyConfiguration.FromEnvironment();
        Assert.Equal(AwsReadOnlyConfiguration.RequiredProfile, configuration.ProfileName);
        Assert.True(configuration.LiveAwsEnabled);
        Assert.False(configuration.DryRun);
        Assert.False(string.IsNullOrWhiteSpace(configuration.AuthorizedAccountId));

        var provider = new IAEngineConsumerConfiguration(Directory.GetCurrentDirectory()).CreateAwsReadOnlyProvider(configuration);
        var result = await provider.GetSnapshotAsync(new CloudObservationRequest("infra-sentinel", "lab", configuration.AuthorizedRegions[0], configuration.AuthorizedRegions, [], configuration.Timeout, 500, configuration.MaxAttempts));

        Assert.Equal(CloudCollectionStatus.Collected, result.Status);
        Assert.NotNull(result.Metadata);
        Assert.All(result.Metadata!.Operations, operation => Assert.DoesNotContain("Create", operation.Operation, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class LiveAwsFactAttribute : FactAttribute
{
    public LiveAwsFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(AwsReadOnlyConfiguration.LiveEnableVariable), "true", StringComparison.OrdinalIgnoreCase))
            Skip = $"Set {AwsReadOnlyConfiguration.LiveEnableVariable}=true for the explicit read-only live test.";
    }
}
