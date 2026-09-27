using System.Text.Json;
using System.Text.Json.Serialization;
using IAEngine.Core.Git;
using InfraSentinel.Core.Adapters;

namespace InfraSentinel.Core.IaC;

public sealed record TerraformAnalysisArtifact(
    string ExecutionId,
    string ProjectId,
    string MilestoneId,
    string Adapter,
    string ParserVersion,
    string FixtureId,
    string FixtureVersion,
    NeutralIaCModel? Model,
    IReadOnlyList<string> FilesAnalyzed,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<IaCFinding> Findings,
    IReadOnlyList<IaCFindingSeverity> Severities,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Limitations,
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

public sealed class TerraformAnalysisEvidence(
    string executionId,
    string projectId,
    string milestoneId,
    string fixtureId,
    string fixtureVersion,
    string branch)
{
    private ProjectAdapterAnalysis? adapterAnalysis;
    private TerraformEvaluation? evaluation;
    private readonly List<string> evidence = [];
    private int retries;
    private int remediationCount;
    private string reviewStatus = "NotStarted";
    private string approvalStatus = "Pending";
    private GitCheckpointDecision? checkpointDecision;
    private GitCheckpointResult? checkpointResult;

    public void RecordAnalysis(ProjectAdapterAnalysis result)
    {
        adapterAnalysis = result;
        evidence.AddRange(result.Evidence);
    }

    public void RecordEvaluation(TerraformEvaluation result, bool retry = false, bool remediated = false)
    {
        evaluation = result;
        if (retry) retries++;
        if (remediated) remediationCount++;
        evidence.Add(result.Justification);
    }

    public void RecordReview(bool passed, string detail) { reviewStatus = passed ? "Approved" : "Failed"; evidence.Add($"review:{detail}"); }
    public void RecordApproval(bool approved, string detail) { approvalStatus = approved ? "Approved" : "HumanRequired"; evidence.Add($"approval:{detail}"); }
    public void RecordCheckpoint(GitCheckpointDecision decision, GitCheckpointResult? result) { checkpointDecision = decision; checkpointResult = result; evidence.Add($"checkpoint:{decision.Status}:{decision.Reason}"); }

    public TerraformAnalysisArtifact BuildArtifact()
    {
        var model = evaluation?.Model;
        var findings = evaluation?.Findings ?? [];
        var status = checkpointResult?.Succeeded == true && checkpointDecision?.Status == GitCheckpointDecisionStatus.Allowed
            ? "Approved"
            : checkpointDecision?.Status == GitCheckpointDecisionStatus.HumanRequired || findings.Any(finding => finding.RequiresHumanApproval)
                ? "HumanRequired"
                : evaluation?.Status == IaCAnalysisStatus.Analyzed && findings.Count == 0 ? "Analyzed" : "Blocked";
        return new(executionId, projectId, milestoneId, adapterAnalysis?.AdapterId ?? "none", adapterAnalysis?.AdapterVersion ?? "0", fixtureId, fixtureVersion,
            model, model?.AnalyzedFiles ?? [], evaluation?.RulesEvaluated ?? [], findings,
            findings.Select(finding => finding.Severity).Distinct().Order().ToArray(), evidence.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            model?.Limitations ?? adapterAnalysis?.Limitations ?? [], retries, remediationCount, reviewStatus, approvalStatus, checkpointDecision,
            checkpointResult?.CommitSha, branch, checkpointResult?.FilesIncluded ?? checkpointDecision?.FilesEvaluated ?? [], checkpointResult?.PushPerformed ?? false,
            checkpointResult?.MergePerformed ?? false, status, checkpointResult?.FailureReason ?? evaluation?.Justification ?? adapterAnalysis?.Reason ?? "No analysis result.");
    }
}

public sealed class TerraformAnalysisArtifactWriter(string artifactPath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(TerraformAnalysisEvidence evidence, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Terraform artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(evidence.BuildArtifact(), Options), cancellationToken);
    }
}
