using System.Text.Json;
using System.Text.Json.Serialization;
using IAEngine.Core.Git;
using InfraSentinel.Core.IaC;

namespace InfraSentinel.Core.Cloud;

public sealed record CloudObservationArtifact(
    string ExecutionId,
    string ProjectId,
    string MilestoneId,
    CloudObservationSourceType SourceType,
    string Provider,
    string SnapshotId,
    string SnapshotVersion,
    NeutralIaCModel? IacReference,
    IReadOnlyList<CloudResourceSnapshot> ResourcesObserved,
    IReadOnlyList<IaCResource> ResourcesExpected,
    IReadOnlyList<CloudObservationFinding> Divergences,
    IReadOnlyList<CloudObservationFinding> Findings,
    IReadOnlyList<CloudFindingSeverity> Severities,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<McpToolCallEvidence> ProviderCalls,
    int RetryAttempts,
    int RemediationCount,
    string ReviewStatus,
    string ApprovalStatus,
    GitCheckpointDecision? CheckpointDecision,
    string? CommitSha,
    string Branch,
    IReadOnlyList<string> ModifiedFiles,
    bool PushPerformed,
    bool MergePerformed,
    CloudObservationStatus Status,
    string Reason);

public sealed class CloudObservationEvidence(
    string executionId,
    string projectId,
    string milestoneId,
    string branch)
{
    private CloudObservationResult? observation;
    private CloudObservationEvaluation? evaluation;
    private NeutralIaCModel? iac;
    private readonly List<McpToolCallEvidence> calls = [];
    private int retries;
    private int remediationCount;
    private string reviewStatus = "NotStarted";
    private string approvalStatus = "Pending";
    private GitCheckpointDecision? decision;
    private GitCheckpointResult? result;

    public void RecordObservation(CloudObservationResult value, NeutralIaCModel desired, IEnumerable<McpToolCallEvidence>? providerCalls = null)
    {
        observation = value;
        iac = desired;
        if (providerCalls is not null) calls.AddRange(providerCalls);
    }
    public void RecordEvaluation(CloudObservationEvaluation value, bool retry = false, bool remediated = false)
    {
        evaluation = value;
        if (retry) retries++;
        if (remediated) remediationCount++;
    }
    public void RecordRetry() => retries++;
    public void RecordReview(bool passed, string detail) => reviewStatus = passed ? "Approved" : "Failed";
    public void RecordApproval(bool approved, string detail) => approvalStatus = approved ? "Approved" : "HumanRequired";
    public void RecordCheckpoint(GitCheckpointDecision value, GitCheckpointResult? checkpointResult) { decision = value; result = checkpointResult; }

    public CloudObservationArtifact BuildArtifact()
    {
        var snapshot = observation?.Snapshot;
        var findings = evaluation?.Findings ?? [];
        var status = result?.Succeeded == true && decision?.Status == GitCheckpointDecisionStatus.Allowed
            ? CloudObservationStatus.Approved
            : decision?.Status == GitCheckpointDecisionStatus.HumanRequired || evaluation?.Status == CloudObservationStatus.HumanRequired
                ? CloudObservationStatus.HumanRequired
                : evaluation?.Status ?? (observation?.Status == CloudCollectionStatus.Collected ? CloudObservationStatus.Unknown : CloudObservationStatus.Failed);
        var limitations = (observation?.Limitations ?? []).Concat(evaluation?.Limitations ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(executionId, projectId, milestoneId, snapshot?.SourceType ?? CloudObservationSourceType.Synthetic, snapshot?.Provider ?? "synthetic-cloud", snapshot?.SnapshotId ?? "none", snapshot?.Version ?? "0", iac,
            snapshot?.Resources ?? [], iac?.Resources ?? [], findings, findings, findings.Select(finding => finding.Severity).Distinct().Order().ToArray(), limitations, calls.OrderBy(call => call.ToolName, StringComparer.Ordinal).ToArray(), retries, remediationCount, reviewStatus, approvalStatus, decision, result?.CommitSha, branch, result?.FilesIncluded ?? decision?.FilesEvaluated ?? [], result?.PushPerformed ?? false, result?.MergePerformed ?? false, status, result?.FailureReason ?? evaluation?.Reason ?? observation?.Reason ?? "No observation result.");
    }
}

public sealed class CloudObservationArtifactWriter(string artifactPath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public async Task WriteAsync(CloudObservationEvidence evidence, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Cloud artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(evidence.BuildArtifact(), Options), cancellationToken);
    }
}
