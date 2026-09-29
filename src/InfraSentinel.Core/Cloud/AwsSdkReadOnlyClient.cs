using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.EKS;
using Amazon.EKS.Model;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.RDS;
using Amazon.RDS.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Task = System.Threading.Tasks.Task;

namespace InfraSentinel.Core.Cloud;

public sealed class AwsSdkReadOnlyClient : IAwsReadOnlyClient
{
    private readonly IAwsReadOnlyPolicy policy;

    public AwsSdkReadOnlyClient(IAwsReadOnlyPolicy? policy = null)
        => this.policy = policy ?? new AwsReadOnlyPolicy();

    public async Task<AwsIdentityObservation> GetIdentityAsync(string profileName, CancellationToken cancellationToken = default)
    {
        EnsureProfile(profileName);
        using var client = new AmazonSecurityTokenServiceClient(LoadCredentials(profileName), new AmazonSecurityTokenServiceConfig { MaxErrorRetry = 0 });
        var response = await client.GetCallerIdentityAsync(new GetCallerIdentityRequest(), cancellationToken);
        return new(response.Account ?? "", response.UserId ?? "", response.Arn ?? "");
    }

    public async Task<AwsReadOnlyClientResult> ObserveAsync(string profileName, IReadOnlyList<string> regions, IReadOnlySet<AwsObservedService> services, TimeSpan timeout, int maxAttempts, CancellationToken cancellationToken = default)
    {
        EnsureProfile(profileName);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var collector = new Collector(maxAttempts);
        var credentials = LoadCredentials(profileName);
        try
        {
            if (services.Contains(AwsObservedService.S3))
                await ObserveS3Async(credentials, collector, timeoutSource.Token);
            foreach (var region in regions.Order(StringComparer.Ordinal))
            {
                if (services.Contains(AwsObservedService.Ec2)) await ObserveEc2Async(credentials, region, collector, timeoutSource.Token);
                if (services.Contains(AwsObservedService.Rds)) await ObserveRdsAsync(credentials, region, collector, timeoutSource.Token);
                if (services.Contains(AwsObservedService.Lambda)) await ObserveLambdaAsync(credentials, region, collector, timeoutSource.Token);
                if (services.Contains(AwsObservedService.Ecs)) await ObserveEcsAsync(credentials, region, collector, timeoutSource.Token);
                if (services.Contains(AwsObservedService.Eks)) await ObserveEksAsync(credentials, region, collector, timeoutSource.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            collector.TimedOut = true;
        }
        return collector.Build();
    }

    private async Task ObserveEc2Async(AWSCredentials credentials, string region, Collector collector, CancellationToken cancellationToken)
    {
        using var client = new AmazonEC2Client(credentials, Config<AmazonEC2Config>(region));
        var instances = await collector.CallAsync("EC2.DescribeInstances", region, () => client.DescribeInstancesAsync(new DescribeInstancesRequest(), cancellationToken), cancellationToken);
        if (instances is not null)
            foreach (var instance in instances.Reservations.SelectMany(item => item.Instances))
                collector.Add(new(instance.InstanceId ?? "unknown", "ec2-instance", region, instance.PublicIpAddress is not null, null, null, null, null,
                    new Dictionary<string, string> { ["state"] = instance.State?.Name?.Value ?? "unknown", ["instanceType"] = instance.InstanceType?.Value ?? "unknown" }, []));
        var addresses = await collector.CallAsync("EC2.DescribeAddresses", region, () => client.DescribeAddressesAsync(new DescribeAddressesRequest(), cancellationToken), cancellationToken);
        if (addresses is not null)
            foreach (var address in addresses.Addresses)
                collector.Add(new(address.AllocationId ?? "unknown", "elastic-ip", region, true, null, null, null, null, new Dictionary<string, string> { ["associated"] = (address.AssociationId is not null).ToString() }, ["elastic-ip-may-incur-cost"]));
        var natGateways = await collector.CallAsync("EC2.DescribeNatGateways", region, () => client.DescribeNatGatewaysAsync(new DescribeNatGatewaysRequest(), cancellationToken), cancellationToken);
        if (natGateways is not null)
            foreach (var gateway in natGateways.NatGateways.Where(item => !string.Equals(item.State?.Value, "deleted", StringComparison.OrdinalIgnoreCase) && !string.Equals(item.State?.Value, "deleting", StringComparison.OrdinalIgnoreCase)))
                collector.Add(new(gateway.NatGatewayId ?? "unknown", "nat-gateway", region, false, null, null, null, null, new Dictionary<string, string> { ["state"] = gateway.State?.Value ?? "unknown" }, ["nat-gateway-may-incur-cost"]));
        var groups = await collector.CallAsync("EC2.DescribeSecurityGroups", region, () => client.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest(), cancellationToken), cancellationToken);
        if (groups is not null)
            foreach (var group in groups.SecurityGroups)
            {
                var publicIngress = group.IpPermissions.Any(permission => permission.Ipv4Ranges.Any(range => range.CidrIp == "0.0.0.0/0") || permission.Ipv6Ranges.Any(range => range.CidrIpv6 == "::/0"));
                collector.Add(new(group.GroupId ?? "unknown", "security-group", region, publicIngress, null, null, null, null, new Dictionary<string, string> { ["name"] = group.GroupName ?? "unknown" }, publicIngress ? ["public-ingress"] : []));
            }
    }

    private async Task ObserveRdsAsync(AWSCredentials credentials, string region, Collector collector, CancellationToken cancellationToken)
    {
        using var client = new AmazonRDSClient(credentials, Config<AmazonRDSConfig>(region));
        var response = await collector.CallAsync("RDS.DescribeDBInstances", region, () => client.DescribeDBInstancesAsync(new DescribeDBInstancesRequest(), cancellationToken), cancellationToken);
        if (response is null) return;
        foreach (var database in response.DBInstances)
            collector.Add(new(database.DBInstanceIdentifier ?? "unknown", "rds-instance", region, database.PubliclyAccessible, database.StorageEncrypted, null, database.BackupRetentionPeriod > 0, null,
                new Dictionary<string, string> { ["engine"] = database.Engine ?? "unknown", ["status"] = database.DBInstanceStatus ?? "unknown" }, []));
    }

    private async Task ObserveLambdaAsync(AWSCredentials credentials, string region, Collector collector, CancellationToken cancellationToken)
    {
        using var client = new AmazonLambdaClient(credentials, Config<AmazonLambdaConfig>(region));
        var response = await collector.CallAsync("Lambda.ListFunctions", region, () => client.ListFunctionsAsync(new ListFunctionsRequest(), cancellationToken), cancellationToken);
        if (response is null) return;
        foreach (var function in response.Functions)
            collector.Add(new(function.FunctionName ?? "unknown", "lambda-function", region, null, function.KMSKeyArn is not null, function.TracingConfig?.Mode is not null, null, null,
                new Dictionary<string, string> { ["runtime"] = function.Runtime?.Value ?? "unknown" }, []));
    }

    private async Task ObserveEcsAsync(AWSCredentials credentials, string region, Collector collector, CancellationToken cancellationToken)
    {
        using var client = new AmazonECSClient(credentials, Config<AmazonECSConfig>(region));
        var clusters = await collector.CallAsync("ECS.ListClusters", region, () => client.ListClustersAsync(new Amazon.ECS.Model.ListClustersRequest(), cancellationToken), cancellationToken);
        if (clusters is null) return;
        foreach (var cluster in clusters.ClusterArns)
        {
            collector.Add(new(cluster, "ecs-cluster", region, null, null, null, null, null, new Dictionary<string, string>(), []));
            var services = await collector.CallAsync("ECS.ListServices", region, () => client.ListServicesAsync(new ListServicesRequest { Cluster = cluster }, cancellationToken), cancellationToken);
            if (services is not null)
                foreach (var service in services.ServiceArns)
                    collector.Add(new(service, "ecs-service", region, null, null, null, null, null, new Dictionary<string, string> { ["cluster"] = cluster }, []));
        }
    }

    private async Task ObserveEksAsync(AWSCredentials credentials, string region, Collector collector, CancellationToken cancellationToken)
    {
        using var client = new AmazonEKSClient(credentials, Config<AmazonEKSConfig>(region));
        var clusters = await collector.CallAsync("EKS.ListClusters", region, () => client.ListClustersAsync(new Amazon.EKS.Model.ListClustersRequest(), cancellationToken), cancellationToken);
        if (clusters is null) return;
        foreach (var cluster in clusters.Clusters)
            collector.Add(new(cluster, "eks-cluster", region, null, null, null, null, null, new Dictionary<string, string>(), []));
    }

    private async Task ObserveS3Async(AWSCredentials credentials, Collector collector, CancellationToken cancellationToken)
    {
        using var client = new AmazonS3Client(credentials, new AmazonS3Config { MaxErrorRetry = 0 });
        var buckets = await collector.CallAsync("S3.ListBuckets", "global", () => client.ListBucketsAsync(new ListBucketsRequest(), cancellationToken), cancellationToken);
        if (buckets is null) return;
        foreach (var bucket in buckets.Buckets)
        {
            var name = bucket.BucketName ?? "unknown";
            await collector.CallAsync("S3.GetBucketLocation", "global", () => client.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = name }, cancellationToken), cancellationToken);
            var encryption = await collector.CallAsync("S3.GetBucketEncryption", "global", () => client.GetBucketEncryptionAsync(new GetBucketEncryptionRequest { BucketName = name }, cancellationToken), cancellationToken);
            var publicAccess = await collector.CallAsync("S3.GetPublicAccessBlock", "global", () => client.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest { BucketName = name }, cancellationToken), cancellationToken);
            collector.Add(new(name, "s3-bucket", "global", publicAccess is null ? null : publicAccess.PublicAccessBlockConfiguration is null, encryption is null ? null : encryption.ServerSideEncryptionConfiguration is not null, null, null, null, new Dictionary<string, string>(), []));
        }
    }

    private static void EnsureProfile(string profileName)
    {
        if (!string.Equals(profileName, AwsReadOnlyConfiguration.RequiredProfile, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the authorized personal AWS profile may be used.");
    }

    private static AWSCredentials LoadCredentials(string profileName)
    {
        var chain = new CredentialProfileStoreChain();
        if (!chain.TryGetAWSCredentials(profileName, out var credentials))
            throw new InvalidOperationException("The authorized AWS profile could not be resolved.");
        return credentials;
    }

    private static TConfig Config<TConfig>(string region) where TConfig : ClientConfig, new()
        => new() { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region), MaxErrorRetry = 0 };

    private sealed class Collector(int maxAttempts)
    {
        private readonly List<AwsResourceObservation> resources = [];
        private readonly List<AwsReadOnlyOperationObservation> operations = [];
        private readonly List<string> limitations = [];
        private readonly List<string> failures = [];
        public int Retries { get; private set; }
        public bool TimedOut { get; set; }

        public void Add(AwsResourceObservation resource) => resources.Add(resource);

        public async Task<T?> CallAsync<T>(string operation, string region, Func<Task<T>> call, CancellationToken cancellationToken) where T : class
        {
            if (operation is not ("STS.GetCallerIdentity" or "EC2.DescribeInstances" or "EC2.DescribeAddresses" or "EC2.DescribeNatGateways" or "EC2.DescribeSecurityGroups" or "RDS.DescribeDBInstances" or "Lambda.ListFunctions" or "S3.ListBuckets" or "S3.GetBucketLocation" or "S3.GetBucketEncryption" or "S3.GetPublicAccessBlock" or "ECS.ListClusters" or "ECS.ListServices" or "EKS.ListClusters"))
                throw new InvalidOperationException("AWS operation is outside the read-only allowlist.");
            for (var attempt = 1; attempt <= Math.Clamp(maxAttempts, 1, 3); attempt++)
            {
                try
                {
                    var response = await call();
                    operations.Add(new(operation, region, "success", Count(response)));
                    return response;
                }
                catch (AmazonServiceException exception) when (attempt < maxAttempts && (exception.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.InternalServerError or System.Net.HttpStatusCode.ServiceUnavailable))
                {
                    Retries++;
                }
                catch (AmazonServiceException exception)
                {
                    var limitation = exception.StatusCode == System.Net.HttpStatusCode.Forbidden || exception.ErrorCode?.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) == true
                        ? $"{operation}:access-denied"
                        : $"{operation}:failed";
                    limitations.Add(limitation);
                    failures.Add(limitation);
                    operations.Add(new(operation, region, "limited", 0));
                    return null;
                }
            }
            limitations.Add($"{operation}:retry-limit");
            failures.Add($"{operation}:retry-limit");
            operations.Add(new(operation, region, "failed", 0));
            return null;
        }

        public AwsReadOnlyClientResult Build()
            => new(resources.OrderBy(resource => resource.Region, StringComparer.Ordinal).ThenBy(resource => resource.ResourceType, StringComparer.Ordinal).ThenBy(resource => resource.ResourceId, StringComparer.Ordinal).ToArray(), operations, limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), Retries, TimedOut);

        private static int Count<T>(T response)
            => response switch
            {
                DescribeInstancesResponse value => value.Reservations.Sum(item => item.Instances.Count),
                DescribeAddressesResponse value => value.Addresses.Count,
                DescribeNatGatewaysResponse value => value.NatGateways.Count,
                DescribeSecurityGroupsResponse value => value.SecurityGroups.Count,
                DescribeDBInstancesResponse value => value.DBInstances.Count,
                ListFunctionsResponse value => value.Functions.Count,
                ListBucketsResponse value => value.Buckets.Count,
                Amazon.ECS.Model.ListClustersResponse value => value.ClusterArns.Count,
                ListServicesResponse value => value.ServiceArns.Count,
                Amazon.EKS.Model.ListClustersResponse value => value.Clusters.Count,
                _ => 0
            };
    }
}
