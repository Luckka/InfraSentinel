using System.Text.Json;
using System.Text.Json.Serialization;
using IAEngine.Core.Git;

namespace InfraSentinel.Core.Adapters;

public sealed record ProjectAdapterArtifact(
    string ExecutionId,
    string MilestoneId,
    string ProjectId,
    string AdapterId,
    string AdapterVersion,
    string ProjectType,
    ProjectAdapterStatus AnalysisStatus,
    NeutralProjectModel? Model,
    IReadOnlyList<string> FilesAnalyzed,
    IReadOnlyList<string> FilesIgnored,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> ValidatorsExecuted,
    IReadOnlyList<ProjectAdapterFinding> Findings,
    IReadOnlyList<string> Evidence,
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
    string Status,
    string Reason);

public sealed class ProjectAdapterEvidence(
    string executionId,
    string milestoneId,
    string projectId,
    string branch)
{
    private ProjectAdapterAnalysis? analysis;
    private readonly List<string> validators = [];
    private readonly List<string> evidence = [];
    private int retries;
    private int remediationCount;
    private string reviewStatus = "NotStarted";
    private string approvalStatus = "Pending";
    private GitCheckpointDecision? checkpointDecision;
    private GitCheckpointResult? checkpointResult;

    public void RecordAnalysis(ProjectAdapterAnalysis result)
    {
        analysis = result;
        evidence.AddRange(result.Evidence);
    }

    public void RecordValidation(string validatorId, bool passed, string detail, bool retry = false, bool remediated = false)
    {
        if (!validators.Contains(validatorId, StringComparer.Ordinal)) validators.Add(validatorId);
        if (retry) retries++;
        if (remediated) remediationCount++;
        evidence.Add($"{validatorId}:{(passed ? "passed" : "failed")}:{detail}");
    }

    public void RecordReview(bool passed, string detail) { reviewStatus = passed ? "Approved" : "Failed"; evidence.Add($"review:{detail}"); }
    public void RecordApproval(bool approved, string detail) { approvalStatus = approved ? "Approved" : "HumanRequired"; evidence.Add($"approval:{detail}"); }

    public void RecordCheckpoint(GitCheckpointDecision decision, GitCheckpointResult? result)
    {
        checkpointDecision = decision;
        checkpointResult = result;
        evidence.Add($"checkpoint:{decision.Status}:{decision.Reason}");
    }

    public ProjectAdapterArtifact BuildArtifact()
    {
        var current = analysis ?? new(ProjectAdapterStatus.Failed, "none", "0", "unknown", null, [], ["Analysis was not executed."], [], "Analysis was not executed.");
        var status = checkpointResult?.Succeeded == true && checkpointDecision?.Status == GitCheckpointDecisionStatus.Allowed
            ? "Approved"
            : checkpointDecision?.Status == GitCheckpointDecisionStatus.HumanRequired || current.Findings.Any(finding => finding.RequiresHumanApproval)
                ? "HumanRequired"
                : current.Status == ProjectAdapterStatus.Analyzed && current.Findings.Count == 0 ? "Analyzed" : "Blocked";
        return new(executionId, milestoneId, projectId, current.AdapterId, current.AdapterVersion, current.ProjectType,
            current.Status, current.Model, current.Model?.AnalyzedFiles ?? [], current.Model?.IgnoredFiles ?? [], current.Limitations,
            validators.Order(StringComparer.Ordinal).ToArray(), current.Findings, evidence.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            retries, remediationCount, reviewStatus, approvalStatus, checkpointDecision, checkpointResult?.CommitSha, branch,
            checkpointResult?.FilesIncluded ?? checkpointDecision?.FilesEvaluated ?? [], checkpointResult?.PushPerformed ?? false,
            checkpointResult?.MergePerformed ?? false, status, checkpointResult?.FailureReason ?? current.Reason);
    }
}

public sealed class ProjectAdapterArtifactWriter(string artifactPath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(ProjectAdapterEvidence evidence, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Adapter artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(evidence.BuildArtifact(), Options), cancellationToken);
    }
}
