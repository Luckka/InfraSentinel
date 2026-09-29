using System.Text.RegularExpressions;

namespace InfraSentinel.Core.Cloud;

public sealed class AwsReadOnlyPolicy : IAwsReadOnlyPolicy
{
    private static readonly HashSet<string> AllowedOperations = new(StringComparer.Ordinal)
    {
        "STS.GetCallerIdentity",
        "EC2.DescribeInstances",
        "EC2.DescribeAddresses",
        "EC2.DescribeNatGateways",
        "EC2.DescribeSecurityGroups",
        "RDS.DescribeDBInstances",
        "Lambda.ListFunctions",
        "S3.ListBuckets",
        "S3.GetBucketLocation",
        "S3.GetBucketEncryption",
        "S3.GetPublicAccessBlock",
        "ECS.ListClusters",
        "ECS.ListServices",
        "EKS.ListClusters"
    };

    private static readonly Regex CredentialPattern = new("(?i)(access[_-]?key|secret|session[_-]?token|password|credential|authorization|bearer)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool IsProfileAllowed(string profileName)
        => string.Equals(profileName, AwsReadOnlyConfiguration.RequiredProfile, StringComparison.Ordinal);

    public bool IsAccountAllowed(string accountId, string authorizedAccountId)
        => !string.IsNullOrWhiteSpace(accountId) && string.Equals(accountId, authorizedAccountId, StringComparison.Ordinal);

    public bool IsRegionAllowed(string region, IReadOnlyList<string> authorizedRegions)
        => authorizedRegions.Contains(region, StringComparer.Ordinal);

    public bool IsOperationAllowed(string operation)
        => AllowedOperations.Contains(operation);

    public bool ContainsCredentialMaterial(IEnumerable<string?> values)
        => values.Any(value => !string.IsNullOrWhiteSpace(value) && CredentialPattern.IsMatch(value));
}
