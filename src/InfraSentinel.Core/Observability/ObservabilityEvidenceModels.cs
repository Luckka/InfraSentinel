using IAEngine.Core.Git;

namespace InfraSentinel.Core.Observability;

public enum ObservabilityEvidenceStatus
{
    Approved,
    HumanRequired,
    Blocked,
    Failed
}

public enum ObservabilityEvidenceEventKind
{
    Task,
    Validation,
    Retry,
    Remediation,
    Review,
    Approval,
    Checkpoint
}

public sealed record ObservabilityEvidenceFinding(
    string RuleId,
    string? ResourceId,
    string Severity,
    string Explanation,
    IReadOnlyList<string> Evidence);

public sealed record ObservabilityEvidenceEvent(
    int Sequence,
    ObservabilityEvidenceEventKind Kind,
    string MilestoneId,
    string TaskId,
    string Detail,
    int Attempt,
    IReadOnlyList<ObservabilityEvidenceFinding> Findings,
    DateTimeOffset Timestamp);

public sealed record ObservabilityEvidenceArtifact(
    string SchemaVersion,
    string ExecutionId,
    string ProjectId,
    string MilestoneId,
    IReadOnlyList<string> TaskIds,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string FixtureId,
    string FixtureVersion,
    string EngineRevision,
    IReadOnlyList<string> ValidationsExecuted,
    IReadOnlyList<ObservabilityEvidenceEvent> Events,
    IReadOnlyList<ObservabilityEvidenceFinding> Findings,
    IReadOnlyList<string> Severities,
    int Attempts,
    int Retries,
    int RemediationCount,
    string ReviewStatus,
    string ApprovalStatus,
    GitCheckpointDecision? CheckpointDecision,
    string? CommitSha,
    string Branch,
    IReadOnlyList<string> ModifiedFiles,
    bool PushPerformed,
    bool MergePerformed,
    ObservabilityEvidenceStatus Status,
    string Reason);

public sealed record ObservabilityEvidenceSnapshot(
    string ExecutionId,
    string ProjectId,
    string MilestoneId,
    IReadOnlyList<string> TaskIds,
    string FixtureId,
    string FixtureVersion,
    string EngineRevision,
    IReadOnlyList<ObservabilityEvidenceEvent> Events,
    string ReviewStatus,
    string ApprovalStatus,
    GitCheckpointDecision? CheckpointDecision,
    string? CommitSha,
    string Branch,
    IReadOnlyList<string> ModifiedFiles,
    bool PushPerformed,
    bool MergePerformed,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt);

public sealed record ObservabilityEvidenceValidation(
    bool Passed,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<ObservabilityEvidenceFinding> Findings,
    string Reason);
