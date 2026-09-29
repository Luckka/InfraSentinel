namespace InfraSentinel.Core.Cloud;

public enum CloudObservationSourceType { Synthetic, Local, External }
public enum CloudCollectionStatus { Collected, Unknown, Failed, TimedOut }
public enum CloudObservationStatus { Approved, HumanRequired, Unknown, Blocked, Failed }
public enum CloudFindingSeverity { Low, Medium, High, Critical }

public sealed record CloudObservedOperation(
    string Operation,
    string Region,
    string Outcome,
    int ResourceCount);

public sealed record CloudObservationMetadata(
    string AccountHash,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Services,
    IReadOnlyList<CloudObservedOperation> Operations,
    IReadOnlyList<string> Failures,
    int Retries,
    bool TimedOut);

public sealed record CloudObservationRequest(
    string ProjectId,
    string Environment,
    string LogicalRegion,
    IReadOnlyList<string> AllowedScope,
    IReadOnlyList<string> RequestedResources,
    TimeSpan Timeout,
    int MaxResources,
    int MaxAttempts = 1);

public sealed record CloudResourceSnapshot(
    string ResourceId,
    string ResourceType,
    string Region,
    string Environment,
    bool? PublicExposure,
    bool? EncryptionEnabled,
    bool? LoggingEnabled,
    bool? BackupEnabled,
    string? Owner,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Limitations);

public sealed record CloudInfrastructureSnapshot(
    string SnapshotId,
    string Version,
    string Provider,
    string AccountAlias,
    string Region,
    string Environment,
    DateTimeOffset CollectedAt,
    CloudObservationSourceType SourceType,
    IReadOnlyList<CloudResourceSnapshot> Resources,
    IReadOnlyList<string> NetworkExposure,
    IReadOnlyList<string> Encryption,
    IReadOnlyList<string> Logging,
    IReadOnlyList<string> Backup,
    IReadOnlyList<string> Owners,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Limitations,
    CloudCollectionStatus CollectionStatus);

public sealed record CloudObservationResult(
    CloudCollectionStatus Status,
    CloudInfrastructureSnapshot? Snapshot,
    IReadOnlyList<string> Limitations,
    string Reason,
    bool IsSynthetic,
    CloudObservationMetadata? Metadata = null);

public interface ICloudSnapshotSource
{
    Task<CloudObservationResult> GetSnapshotAsync(CloudObservationRequest request, CancellationToken cancellationToken = default);
}

public interface ICloudObservationProvider : ICloudSnapshotSource
{
    string ProviderId { get; }
    CloudObservationSourceType SourceType { get; }
}

public sealed record CloudObservationFinding(
    string RuleId,
    string Resource,
    string DesiredState,
    string ObservedState,
    CloudFindingSeverity Severity,
    string Explanation,
    IReadOnlyList<string> Evidence,
    string? Limitation,
    string RemediationGuidance,
    bool RequiresHumanApproval);

public sealed record CloudObservationEvaluation(
    CloudObservationStatus Status,
    IReadOnlyList<CloudObservationFinding> Findings,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<string> Limitations,
    string Reason)
{
    public bool Approved => Status == CloudObservationStatus.Approved;
}
