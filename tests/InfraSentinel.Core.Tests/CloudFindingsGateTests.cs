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

public sealed class CloudFindingsGateTests
{
    [Fact]
    public void SafeSnapshotIsApproved()
    {
        var result = Evaluate(SafeSnapshot());

        Assert.Equal(CloudFindingsGateStatus.Approved, result.Status);
        Assert.Empty(result.Findings);
        Assert.All(result.RuleEvaluations, evaluation => Assert.Contains(evaluation.Status, new[] { CloudFindingEvaluationStatus.Pass, CloudFindingEvaluationStatus.NotApplicable }));
    }

    [Fact]
    public void SecurityRulesProduceExpectedFindingsAndSeverities()
    {
        var resource = Resource("public", "security-group", publicExposure: true, properties: Props(("adminPortOpen", "true"), ("leastPrivilege", "false"), ("critical", "true")));
        var result = Evaluate(Snapshot(resource));

        Assert.Equal(CloudFindingsGateStatus.HumanRequired, result.Status);
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-PUBLIC-EXPOSURE" && finding.Severity == CloudFindingSeverity.Critical);
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-ADMIN-PORT" && finding.Severity == CloudFindingSeverity.Critical);
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-LEAST-PRIVILEGE" && finding.Severity == CloudFindingSeverity.High);
        Assert.All(result.Findings, finding => Assert.False(finding.Remediation.AutomaticExecutionAllowed));
    }

    [Fact]
    public void SecurityRulesCoverEncryptionBucketOwnerEnvironmentRegionAndClassification()
    {
        var encryption = Resource("encrypted", "rds-instance", encryptionEnabled: false, owner: "owner", backupEnabled: true, properties: Props(("classification", "database"), ("critical", "false")));
        var bucket = Resource("bucket", "s3-bucket", publicExposure: true, owner: "owner", properties: Props(("classification", "storage")));
        var production = Resource("production", "ec2-instance", environment: "production", owner: null, properties: Props(("critical", "true")));
        var outside = Resource("outside", "ec2-instance", region: "sa-east-1", owner: "owner", properties: Props(("classification", "compute")));
        var unknown = Resource("unknown", "unknown", owner: "owner", properties: Props());
        var result = Evaluate(Snapshot(encryption, bucket, production, outside, unknown));

        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-ENCRYPTION");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-BUCKET-PUBLIC-BLOCK");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-CRITICAL-OWNER");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-PRODUCTION-IDENTITY");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-REGION-ALLOWLIST");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "SECURITY-CLASSIFICATION");
    }

    [Fact]
    public void ResilienceRulesCoverMultiAzBackupRetentionDependenciesRecoveryAndSpof()
    {
        var resource = Resource("rds", "rds-instance", backupEnabled: false, owner: null, dependencies: ["payments"], properties: Props(
            ("multiAz", "false"), ("backupRetentionDays", "1"), ("availabilityBehavior", ""), ("redundant", "false"),
            ("critical", "true"), ("recoveryEvidence", ""), ("singlePointOfFailure", "true")));
        var result = Evaluate(Snapshot(resource));

        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "RESILIENCE-RDS-MULTI-AZ");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "RESILIENCE-BACKUP");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "RESILIENCE-RETENTION");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "RESILIENCE-DEPENDENCY-OWNER");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "RESILIENCE-DEPENDENCY-OBSERVABILITY");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "RESILIENCE-RECOVERY-EVIDENCE");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "RESILIENCE-SINGLE-POINT-OF-FAILURE");
    }

    [Fact]
    public void ObservabilityRulesCoverLoggingMetricsAlarmsTraceabilityAndMonitoring()
    {
        var resource = Resource("critical", "ec2-instance", loggingEnabled: false, properties: Props(("critical", "true"), ("metrics", "false"), ("alarms", "false"), ("traceability", ""), ("operationalEvidence", ""), ("monitoring", "")));
        var result = Evaluate(Snapshot(resource));

        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "OBSERVABILITY-LOGGING");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "OBSERVABILITY-METRICS");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "OBSERVABILITY-ALARMS");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "OBSERVABILITY-TRACEABILITY");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "OBSERVABILITY-OPERATIONAL-EVIDENCE");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "OBSERVABILITY-CRITICAL-MONITORING");
    }

    [Fact]
    public void CostAndGovernanceRulesCoverPaidServicesTagsOwnersPurposeAndRegions()
    {
        var nat = Resource("nat", "nat-gateway", region: "sa-east-1", owner: null, properties: Props(("purpose", ""), ("tagsPresent", "false")));
        var eip = Resource("eip", "elastic-ip", owner: "owner", properties: Props(("associated", "false"), ("purpose", "lab")));
        var rds = Resource("rds", "rds-instance", owner: "owner", properties: Props(("purpose", "lab")));
        var loadBalancer = Resource("lb", "load-balancer", owner: "owner", properties: Props(("purpose", "lab")));
        var result = Evaluate(Snapshot(nat, eip, rds, loadBalancer));

        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-NAT-GATEWAY");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-ELASTIC-IP");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-RDS");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-LOAD-BALANCER");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-TAGS");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-OWNER");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-PURPOSE");
        Assert.Contains(result.Findings, finding => finding.RuleIdentifier == "COST-REGION");
    }

    [Fact]
    public void UnknownAndBlockedSnapshotsCannotPass()
    {
        var incomplete = Evaluate(Snapshot(Resource("incomplete", "ec2-instance", properties: Props())));
        var blocked = Evaluate(Snapshot(Resource("failed", "ec2-instance")) with { CollectionStatus = CloudCollectionStatus.Failed });

        Assert.Equal(CloudFindingsGateStatus.Unknown, incomplete.Status);
        Assert.Contains(incomplete.Findings, finding => finding.Status == CloudFindingEvaluationStatus.Unknown);
        Assert.Equal(CloudFindingsGateStatus.Blocked, blocked.Status);
        Assert.Contains(blocked.RuleEvaluations, evaluation => evaluation.Status == CloudFindingEvaluationStatus.Blocked);
    }

    [Fact]
    public void EmptyPartialAccessDeniedAndServiceLimitedSnapshotsAreExplainable()
    {
        var empty = Evaluate(Snapshot());
        var partial = Evaluate(Snapshot(Resource("partial", "ec2-instance")) with { Limitations = ["EC2.DescribeInstances:access-denied"] });
        var limited = Evaluate(Snapshot(Resource("limited", "lambda-function")) with { Limitations = ["Lambda.ListFunctions:service-limited"] });

        Assert.Equal(CloudFindingsGateStatus.Unknown, empty.Status);
        Assert.Contains(empty.Limitations, limitation => limitation.StartsWith("SNAPSHOT-NONEMPTY", StringComparison.Ordinal));
        Assert.Contains(partial.Limitations, limitation => limitation.Contains("EC2.DescribeInstances", StringComparison.Ordinal));
        Assert.Contains(limited.Limitations, limitation => limitation.Contains("Lambda.ListFunctions", StringComparison.Ordinal));
    }

    [Fact]
    public void FindingsAreDeterministicAndSortedIndependentOfSnapshotOrder()
    {
        var resources = new[]
        {
            Resource("medium", "nat-gateway"),
            Resource("critical", "security-group", publicExposure: true, properties: Props(("adminPortOpen", "true"))),
            Resource("high", "rds-instance", encryptionEnabled: false)
        };
        var first = Evaluate(Snapshot(resources));
        var second = Evaluate(Snapshot(resources.Reverse().ToArray()));

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(first.Findings.OrderByDescending(finding => finding.Severity).ThenBy(finding => finding.RuleIdentifier).ThenBy(finding => finding.Resource.ResourceType).ThenBy(finding => finding.Resource.ResourceId).ThenBy(finding => finding.Explanation), first.Findings);
    }

    [Fact]
    public async Task ArtifactPersistsFindingsWithoutSecretsOrMutationFields()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new CloudFindingsEvidence("m19-execution", "infra-sentinel", "cloud-security-findings-gate", "FINDINGS-009", "feature/m19-cloud-findings-gate");
        var writer = configuration.CreateCloudFindingsEvidenceWriter();
        var runner = new CloudFindingsValidationRunner(SafeSnapshot(), configuration.CreateCloudFindingsGate(), evidence, writer);

        var result = await runner.RunAsync();
        var artifact = await File.ReadAllTextAsync(configuration.CloudFindingsArtifactPath);

        Assert.True(result.Single().Passed);
        Assert.Contains("cloud-security-findings-gate", artifact, StringComparison.Ordinal);
        Assert.Contains("\"pushPerformed\": false", artifact, StringComparison.Ordinal);
        Assert.Contains("\"mergePerformed\": false", artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", artifact, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EngineHostUsesM19ReviewApprovalAndCheckpointBoundary()
    {
        using var workspace = new TemporaryWorkspace();
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new CloudFindingsEvidence("m19-host", "infra-sentinel", "cloud-security-findings-gate", "FINDINGS-009", "feature/m19-cloud-findings-gate");
        var writer = configuration.CreateCloudFindingsEvidenceWriter();
        var runner = new CloudFindingsValidationRunner(SafeSnapshot(), configuration.CreateCloudFindingsGate(), evidence, writer);
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
                .RegisterProvider<IReviewAgent>("local-review", () => new CloudFindingsReviewAgent(new LocalReview(), evidence))
                .RegisterValidator<IValidationRunner>("local-validator", () => runner)
                .RegisterCapability<IValidationRunner>("validation", () => runner)
                .RegisterPolicy("local-policy", () => new LocalPolicy()),
            RunStore = new RunStore(workspace.Root, configuration.RunsDirectory),
            Git = new LocalGit(workspace.Root),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory,
            CheckpointCoordinator = new CloudFindingsCheckpointCoordinator(coordinator, evidence, writer),
            CheckpointRequestSource = new CloudFindingsCheckpointRequestSource(configuration.CreateCheckpointRequestSource("feature/m19-cloud-findings-gate", ["cloud-findings-gate.json"], "feat: validate cloud findings gate"), evidence)
        });

        var awaiting = await host.RunMilestoneAsync("cloud-security-findings-gate");
        var approved = await host.ApproveMilestoneAsync("cloud-security-findings-gate");

        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.CompleteAwaitingApproval, awaiting.Status);
        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.Single(coordinator.Commits);
        Assert.False(coordinator.Commits[0].SecretsDetected);
    }

    private static CloudFindingEvaluationResult Evaluate(CloudInfrastructureSnapshot snapshot)
        => new CloudFindingsGate(new HashSet<string>(["us-east-1"], StringComparer.Ordinal)).Evaluate(snapshot);

    private static CloudInfrastructureSnapshot Snapshot(params CloudResourceSnapshot[] resources)
        => new("m19-fixture", "m19-1", "aws-read-only", "aws-account-hash", "us-east-1", "lab", DateTimeOffset.UnixEpoch, CloudObservationSourceType.External, resources, [], [], [], [], [], [], [], [], CloudCollectionStatus.Collected);

    private static CloudInfrastructureSnapshot SafeSnapshot()
        => Snapshot(Resource("safe", "ec2-instance"));

    private static CloudResourceSnapshot Resource(string id, string type, string region = "us-east-1", string environment = "lab", bool? publicExposure = false, bool? encryptionEnabled = true, bool? loggingEnabled = true, bool? backupEnabled = true, string? owner = "team-platform", IReadOnlyList<string>? dependencies = null, IReadOnlyDictionary<string, string>? properties = null)
        => new(id, type, region, environment, publicExposure, encryptionEnabled, loggingEnabled, backupEnabled, owner, [], dependencies ?? [], [$"fixture:{id}"], [], properties ?? SafeProperties());

    private static Dictionary<string, string> SafeProperties(params (string Key, string Value)[] overrides)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["adminPortOpen"] = "false", ["leastPrivilege"] = "true", ["critical"] = "false", ["classification"] = "compute",
            ["multiAz"] = "true", ["backupRetentionDays"] = "30", ["availabilityBehavior"] = "failover", ["redundant"] = "true",
            ["recoveryEvidence"] = "tested", ["singlePointOfFailure"] = "false", ["metrics"] = "true", ["alarms"] = "true",
            ["traceability"] = "correlation-id", ["operationalEvidence"] = "runbook", ["monitoring"] = "dashboard",
            ["tagsPresent"] = "true", ["purpose"] = "lab-validation"
        };
        foreach (var (key, value) in overrides) properties[key] = value;
        return properties;
    }

    private static IReadOnlyDictionary<string, string> Props(params (string Key, string Value)[] values)
        => values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

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
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default) => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));
    }

    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;

    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m19-cloud-findings-gate");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("m19-sha");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }

    private sealed class RecordingCoordinator : IGitCheckpointCoordinator
    {
        public List<GitCheckpointRequest> Commits { get; } = [];
        public Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default) => Task.FromResult(GitCheckpointPolicy.Evaluate(request));
        public Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
        {
            Commits.Add(request);
            return Task.FromResult(new GitCheckpointResult(true, "m19-sha", request.ProposedCommitMessage, request.CurrentBranch, request.ExpectedFiles, DateTimeOffset.UnixEpoch, null, true, false, false));
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("infrasentinel-m19-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
