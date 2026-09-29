using System.Security.Cryptography;
using System.Text;

namespace InfraSentinel.Core.Cloud;

public sealed class AwsReadOnlySnapshotNormalizer : ICloudSnapshotNormalizer
{
    public CloudInfrastructureSnapshot Normalize(AwsIdentityObservation identity, AwsReadOnlyConfiguration configuration, AwsReadOnlyClientResult result)
    {
        if (result.Resources.Any(resource => resource.Properties.Any(item => IsSensitive(item.Key) || IsSensitive(item.Value))))
            throw new InvalidOperationException("sanitized-response-rejected");
        var resources = result.Resources
            .OrderBy(resource => resource.Region, StringComparer.Ordinal)
            .ThenBy(resource => resource.ResourceType, StringComparer.Ordinal)
            .ThenBy(resource => resource.ResourceId, StringComparer.Ordinal)
            .Select(resource => new CloudResourceSnapshot(
                resource.ResourceId,
                resource.ResourceType,
                resource.Region,
                "unknown",
                resource.PublicExposure,
                resource.EncryptionEnabled,
                resource.LoggingEnabled,
                resource.BackupEnabled,
                resource.Owner,
                resource.Properties.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}").ToArray(),
                [],
                [$"aws:{resource.ResourceType}:{resource.ResourceId}"],
                resource.Limitations.Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        var accountHash = Hash(identity.AccountId);
        var snapshotSeed = string.Join('|', resources.Select(resource => $"{resource.Region}:{resource.ResourceType}:{resource.ResourceId}"));
        var snapshotId = $"aws-{Hash(snapshotSeed)}";
        var region = configuration.AuthorizedRegions.Order(StringComparer.Ordinal).FirstOrDefault() ?? "unknown";
        return new(
            snapshotId,
            "m18-1",
            "aws-read-only",
            $"aws-account-{accountHash}",
            region,
            "unknown",
            DateTimeOffset.UnixEpoch,
            CloudObservationSourceType.External,
            resources,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            result.Limitations.Order(StringComparer.Ordinal).ToArray(),
            CloudCollectionStatus.Collected);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""))).ToLowerInvariant()[..12];

    private static bool IsSensitive(string value)
        => value.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("password", StringComparison.OrdinalIgnoreCase)
            || value.Contains("accesskey", StringComparison.OrdinalIgnoreCase)
            || value.Contains("authorization", StringComparison.OrdinalIgnoreCase);
}
