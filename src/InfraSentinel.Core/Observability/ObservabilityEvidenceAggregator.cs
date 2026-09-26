using System.Text.Json;
using System.Text.Json.Serialization;
using IAEngine.Core.Git;

namespace InfraSentinel.Core.Observability;

public sealed class ObservabilityEvidenceAggregator(
    string executionId,
    string projectId,
    string milestoneId,
    IReadOnlyList<string> taskIds,
    string fixtureId,
    string fixtureVersion,
    string engineRevision,
    string branch)
{
    private readonly List<ObservabilityEvidenceEvent> events = [];
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private int attempts;
    private int retries;
    private int remediationCount;
    private string reviewStatus = "NotStarted";
    private string approvalStatus = "Pending";
    private GitCheckpointDecision? checkpointDecision;
    private string? commitSha;
    private IReadOnlyList<string> modifiedFiles = [];
    private bool pushPerformed;
    private bool mergePerformed;
    private string reason = "Evidence collection in progress.";
    private ObservabilityEvidenceStatus status = ObservabilityEvidenceStatus.Failed;

    public void RecordValidation(string taskId, bool passed, string detail, IReadOnlyList<ObservabilityEvidenceFinding>? findings = null)
    {
        attempts++;
        if (attempts > 1)
        {
            retries++;
            Add(ObservabilityEvidenceEventKind.Retry, taskId, "Validation retry recorded.", attempts, []);
            remediationCount++;
            Add(ObservabilityEvidenceEventKind.Remediation, taskId, "Bounded remediation preceded this validation attempt.", attempts, []);
        }
        Add(ObservabilityEvidenceEventKind.Validation, taskId, detail, attempts, findings ?? []);
    }

    public void RecordTask(string taskId, string detail = "Task execution registered.")
        => Add(ObservabilityEvidenceEventKind.Task, taskId, detail, attempts, []);

    public void RecordReview(string taskId, bool passed, string detail)
    {
        reviewStatus = passed ? "Approved" : "Failed";
        Add(ObservabilityEvidenceEventKind.Review, taskId, detail, attempts, []);
    }

    public void RecordApproval(string taskId, bool approved, string detail)
    {
        approvalStatus = approved ? "Approved" : "HumanRequired";
        Add(ObservabilityEvidenceEventKind.Approval, taskId, detail, attempts, []);
    }

    public void RecordCheckpoint(string taskId, GitCheckpointDecision decision, GitCheckpointResult? result)
    {
        checkpointDecision = decision;
        commitSha = result?.CommitSha;
        modifiedFiles = result?.FilesIncluded ?? decision.FilesEvaluated;
        pushPerformed = result?.PushPerformed ?? false;
        mergePerformed = result?.MergePerformed ?? false;
        status = decision.Status == GitCheckpointDecisionStatus.Allowed && result?.Succeeded == true
            ? ObservabilityEvidenceStatus.Approved
            : decision.Status == GitCheckpointDecisionStatus.HumanRequired
                ? ObservabilityEvidenceStatus.HumanRequired
                : ObservabilityEvidenceStatus.Blocked;
        reason = result?.FailureReason ?? decision.Reason;
        Add(ObservabilityEvidenceEventKind.Checkpoint, taskId, reason, attempts, []);
    }

    public ObservabilityEvidenceSnapshot Snapshot()
        => new(executionId, projectId, milestoneId, taskIds, fixtureId, fixtureVersion, engineRevision,
            events.OrderBy(item => item.Sequence).ToArray(), reviewStatus, approvalStatus, checkpointDecision,
            commitSha, branch, modifiedFiles.Order(StringComparer.Ordinal).ToArray(), pushPerformed, mergePerformed,
            startedAt, DateTimeOffset.UtcNow);

    public ObservabilityEvidenceArtifact BuildArtifact()
    {
        var snapshot = Snapshot();
        var findings = snapshot.Events.SelectMany(item => item.Findings).ToArray();
        return new(
            "1.0",
            snapshot.ExecutionId,
            snapshot.ProjectId,
            snapshot.MilestoneId,
            snapshot.TaskIds,
            snapshot.StartedAt,
            snapshot.UpdatedAt,
            snapshot.FixtureId,
            snapshot.FixtureVersion,
            snapshot.EngineRevision,
            snapshot.Events.Where(item => item.Kind == ObservabilityEvidenceEventKind.Validation).Select(item => item.TaskId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            snapshot.Events,
            findings,
            findings.Select(item => item.Severity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            snapshot.Events.Count(item => item.Kind == ObservabilityEvidenceEventKind.Validation),
            snapshot.Events.Count(item => item.Kind == ObservabilityEvidenceEventKind.Retry),
            snapshot.Events.Count(item => item.Kind == ObservabilityEvidenceEventKind.Remediation),
            snapshot.ReviewStatus,
            snapshot.ApprovalStatus,
            snapshot.CheckpointDecision,
            snapshot.CommitSha,
            snapshot.Branch,
            snapshot.ModifiedFiles,
            snapshot.PushPerformed,
            snapshot.MergePerformed,
            snapshot.CheckpointDecision?.Status == GitCheckpointDecisionStatus.Allowed && snapshot.CommitSha is not null
                ? ObservabilityEvidenceStatus.Approved
                : snapshot.ApprovalStatus == "HumanRequired"
                    ? ObservabilityEvidenceStatus.HumanRequired
                    : status,
            snapshot.CheckpointDecision?.Reason ?? reason);
    }

    public string SerializeDeterministically()
        => JsonSerializer.Serialize(BuildArtifact(), new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

    private void Add(ObservabilityEvidenceEventKind kind, string taskId, string detail, int attempt, IReadOnlyList<ObservabilityEvidenceFinding> findings)
        => events.Add(new(events.Count + 1, kind, milestoneId, taskId, detail, attempt, findings, DateTimeOffset.UtcNow));
}
