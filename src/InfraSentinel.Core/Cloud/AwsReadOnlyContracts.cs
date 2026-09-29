using System.Collections.ObjectModel;

namespace InfraSentinel.Core.Cloud;

public enum AwsObservedService
{
    Ec2,
    Rds,
    Lambda,
    S3,
    Ecs,
    Eks
}

public sealed record AwsReadOnlyConfiguration(
    string ProfileName,
    string AuthorizedAccountId,
    IReadOnlyList<string> AuthorizedRegions,
    TimeSpan Timeout,
    int MaxAttempts,
    bool DryRun,
    bool LiveAwsEnabled,
    IReadOnlySet<AwsObservedService> EnabledServices)
{
    public const string RequiredProfile = "personal-infrasentinel";
    public const string LiveEnableVariable = "INFRA_SENTINEL_ENABLE_LIVE_AWS";

    public static AwsReadOnlyConfiguration FromEnvironment(IReadOnlyDictionary<string, string?>? environment = null)
    {
        environment ??= new ReadOnlyDictionary<string, string?>(Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(item => (string)item.Key, item => item.Value?.ToString(), StringComparer.Ordinal));
        var regions = (environment.GetValueOrDefault("INFRA_SENTINEL_AWS_REGIONS") ?? "us-east-1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var timeoutSeconds = int.TryParse(environment.GetValueOrDefault("INFRA_SENTINEL_AWS_TIMEOUT_SECONDS"), out var parsedTimeout) ? parsedTimeout : 30;
        var maxAttempts = int.TryParse(environment.GetValueOrDefault("INFRA_SENTINEL_AWS_MAX_ATTEMPTS"), out var parsedAttempts) ? parsedAttempts : 2;
        return new(
            environment.GetValueOrDefault("INFRA_SENTINEL_AWS_PROFILE") ?? RequiredProfile,
            environment.GetValueOrDefault("INFRA_SENTINEL_AWS_ACCOUNT_ID") ?? "",
            regions,
            TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)),
            Math.Clamp(maxAttempts, 1, 3),
            !string.Equals(environment.GetValueOrDefault("INFRA_SENTINEL_AWS_DRY_RUN"), "false", StringComparison.OrdinalIgnoreCase),
            string.Equals(environment.GetValueOrDefault(LiveEnableVariable), "true", StringComparison.OrdinalIgnoreCase),
            new HashSet<AwsObservedService>(Enum.GetValues<AwsObservedService>()));
    }
}

public sealed record AwsIdentityObservation(string AccountId, string UserId, string Arn);

public sealed record AwsResourceObservation(
    string ResourceId,
    string ResourceType,
    string Region,
    bool? PublicExposure,
    bool? EncryptionEnabled,
    bool? LoggingEnabled,
    bool? BackupEnabled,
    string? Owner,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> Limitations);

public sealed record AwsReadOnlyOperationObservation(
    string Operation,
    string Region,
    string Outcome,
    int ResourceCount);

public sealed record AwsReadOnlyClientResult(
    IReadOnlyList<AwsResourceObservation> Resources,
    IReadOnlyList<AwsReadOnlyOperationObservation> Operations,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> Failures,
    int Retries,
    bool TimedOut);

public interface IAwsReadOnlyPolicy
{
    bool IsProfileAllowed(string profileName);
    bool IsAccountAllowed(string accountId, string authorizedAccountId);
    bool IsRegionAllowed(string region, IReadOnlyList<string> authorizedRegions);
    bool IsOperationAllowed(string operation);
    bool ContainsCredentialMaterial(IEnumerable<string?> values);
}

public interface IAwsIdentityVerifier
{
    Task<AwsIdentityObservation> VerifyAsync(AwsReadOnlyConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface IAwsReadOnlyClient
{
    Task<AwsIdentityObservation> GetIdentityAsync(string profileName, CancellationToken cancellationToken = default);
    Task<AwsReadOnlyClientResult> ObserveAsync(string profileName, IReadOnlyList<string> regions, IReadOnlySet<AwsObservedService> services, TimeSpan timeout, int maxAttempts, CancellationToken cancellationToken = default);
}

public interface ICloudSnapshotNormalizer
{
    CloudInfrastructureSnapshot Normalize(AwsIdentityObservation identity, AwsReadOnlyConfiguration configuration, AwsReadOnlyClientResult result);
}

public interface ICloudEvidenceWriter
{
    Task WriteAsync(CloudObservationEvidence evidence, CancellationToken cancellationToken = default);
}

public sealed class CloudObservationEvidenceWriterAdapter(CloudObservationArtifactWriter inner) : ICloudEvidenceWriter
{
    public Task WriteAsync(CloudObservationEvidence evidence, CancellationToken cancellationToken = default)
        => inner.WriteAsync(evidence, cancellationToken);
}
