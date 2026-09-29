using System.Text.Json;
using System.Text.Json.Serialization;
using IAEngine.Core.Git;
using InfraSentinel.Core.IaC;
using OnlineOs.AiOrchestrator.Hosting;

namespace InfraSentinel.Core.Cloud;

public sealed record CloudFindingsArtifact(
    string ExecutionId,
    string ProjectId,
    string MilestoneId,
    string TaskId,
    string Branch,
    string SnapshotId,
    string AccountIdentifier,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> RulesExecuted,
    IReadOnlyList<CloudRuleEvaluation> RuleEvaluations,
    IReadOnlyList<CloudFinding> Findings,
    IReadOnlyList<CloudFindingSeverity> Severities,
    IReadOnlyList<CloudFindingEvidence> Evidence,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<CloudRemediationRecommendation> Recommendations,
    string ReviewStatus,
    string ApprovalStatus,
    string CheckpointStatus,
    CloudFindingsGateStatus Status,
    bool PushPerformed,
    bool MergePerformed,
    string Reason);

public sealed class CloudFindingsEvidence(
    string executionId,
    string projectId,
    string milestoneId,
    string taskId,
    string branch)
{
    private CloudInfrastructureSnapshot? snapshot;
    private CloudFindingEvaluationResult? evaluation;
    private string reviewStatus = "NotStarted";
    private string approvalStatus = "Pending";
    private GitCheckpointDecision? checkpointDecision;

    public void RecordEvaluation(CloudInfrastructureSnapshot observedSnapshot, CloudFindingEvaluationResult result)
    {
        snapshot = observedSnapshot;
        evaluation = result;
    }

    public void RecordReview(bool passed, string _) => reviewStatus = passed ? "Approved" : "Failed";
    public void RecordApproval(bool approved, string _) => approvalStatus = approved ? "Approved" : "HumanRequired";
    public void RecordCheckpoint(GitCheckpointDecision decision) => checkpointDecision = decision;

    public CloudFindingsArtifact BuildArtifact()
    {
        var currentSnapshot = snapshot;
        var currentEvaluation = evaluation ?? new CloudFindingEvaluationResult(CloudFindingsGateStatus.Blocked, [], [], [], [], ["evaluation-not-recorded"], "Evaluation was not recorded.");
        var findings = currentEvaluation.Findings;
        return new(
            executionId,
            projectId,
            milestoneId,
            taskId,
            branch,
            currentSnapshot?.SnapshotId ?? "none",
            currentSnapshot?.AccountAlias ?? "unknown",
            currentSnapshot is null ? [] : [currentSnapshot.Region],
            currentEvaluation.RulesEvaluated,
            currentEvaluation.RuleEvaluations,
            findings,
            findings.Select(item => item.Severity).Distinct().OrderByDescending(item => item).ToArray(),
            findings.SelectMany(item => item.Evidence).OrderBy(item => item.Source, StringComparer.Ordinal).ThenBy(item => item.Detail, StringComparer.Ordinal).ToArray(),
            currentEvaluation.Limitations.Order(StringComparer.Ordinal).ToArray(),
            findings.Select(item => item.Remediation).Distinct().OrderBy(item => item.Action, StringComparer.Ordinal).ToArray(),
            reviewStatus,
            approvalStatus,
            checkpointDecision?.Status.ToString() ?? "Pending",
            currentEvaluation.Status,
            false,
            false,
            currentEvaluation.Reason);
    }
}

public interface ICloudFindingsEvidenceWriter
{
    Task WriteAsync(CloudFindingsEvidence evidence, CancellationToken cancellationToken = default);
}

public sealed class CloudFindingsEvidenceWriter(string artifactPath) : ICloudFindingsEvidenceWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(CloudFindingsEvidence evidence, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Cloud findings artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(evidence.BuildArtifact(), Options), cancellationToken);
    }
}

public sealed class CloudFindingsValidationRunner(
    CloudInfrastructureSnapshot snapshot,
    CloudFindingsGate gate,
    CloudFindingsEvidence evidence,
    ICloudFindingsEvidenceWriter writer) : OnlineOs.AiOrchestrator.Abstractions.IValidationRunner
{
    public async Task<IReadOnlyList<OnlineOs.AiOrchestrator.Models.ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evaluation = gate.Evaluate(snapshot);
        evidence.RecordEvaluation(snapshot, evaluation);
        await writer.WriteAsync(evidence, cancellationToken);
        var passed = evaluation.Status == CloudFindingsGateStatus.Approved;
        return [new("CloudFindingsGate", true, new OnlineOs.AiOrchestrator.Models.ProcessResult("infrasentinel-cloud-findings", passed ? 0 : 1, evaluation.Reason, "", TimeSpan.Zero), passed ? OnlineOs.AiOrchestrator.Models.ValidationStatus.Pass : OnlineOs.AiOrchestrator.Models.ValidationStatus.Fail, passed ? null : evaluation.Reason)];
    }
}

public sealed class CloudFindingsReviewAgent(OnlineOs.AiOrchestrator.Abstractions.IReviewAgent inner, CloudFindingsEvidence evidence) : OnlineOs.AiOrchestrator.Abstractions.IReviewAgent
{
    public async Task<OnlineOs.AiOrchestrator.Models.ReviewResult> ReviewAsync(OnlineOs.AiOrchestrator.Models.DevelopmentTask task, OnlineOs.AiOrchestrator.Models.EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
    {
        var result = await inner.ReviewAsync(task, engineering, gitDiff, cancellationToken);
        evidence.RecordReview(result.Decision == OnlineOs.AiOrchestrator.Models.ReviewDecision.Pass, result.Summary);
        return result;
    }
}

public sealed class CloudFindingsCheckpointCoordinator(IAEngine.Core.Git.IGitCheckpointCoordinator inner, CloudFindingsEvidence evidence, ICloudFindingsEvidenceWriter writer) : IAEngine.Core.Git.IGitCheckpointCoordinator
{
    public async Task<IAEngine.Core.Git.GitCheckpointDecision> EvaluateAsync(IAEngine.Core.Git.GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await inner.EvaluateAsync(request, cancellationToken);
        evidence.RecordCheckpoint(decision);
        await writer.WriteAsync(evidence, cancellationToken);
        return decision;
    }

    public async Task<IAEngine.Core.Git.GitCheckpointResult> CommitAsync(IAEngine.Core.Git.GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        var result = await inner.CommitAsync(request, cancellationToken);
        evidence.RecordCheckpoint(IAEngine.Core.Git.GitCheckpointPolicy.Evaluate(request));
        await writer.WriteAsync(evidence, cancellationToken);
        return result;
    }
}

public sealed class CloudFindingsCheckpointRequestSource(IGitCheckpointRequestSource inner, CloudFindingsEvidence evidence) : IGitCheckpointRequestSource
{
    public IAEngine.Core.Git.GitCheckpointRequest CreateForTask(OnlineOs.AiOrchestrator.Models.RunRecord run) => inner.CreateForTask(run);

    public IAEngine.Core.Git.GitCheckpointRequest CreateForMilestone(string milestoneId, OnlineOs.AiOrchestrator.Hosting.EngineMilestoneExecutionResult result)
    {
        evidence.RecordApproval(result.Status == OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved, result.Status.ToString());
        return inner.CreateForMilestone(milestoneId, result);
    }
}
