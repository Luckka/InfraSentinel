using IAEngine.Core.Git;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Adapters;

public sealed class ProjectAdapterReviewAgent(IReviewAgent inner, ProjectAdapterEvidence evidence) : IReviewAgent
{
    public async Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
    {
        var result = await inner.ReviewAsync(task, engineering, gitDiff, cancellationToken);
        evidence.RecordReview(result.Decision == ReviewDecision.Pass, result.Summary);
        return result;
    }
}

public sealed class ProjectAdapterCheckpointCoordinator(
    IGitCheckpointCoordinator inner,
    ProjectAdapterEvidence evidence,
    ProjectAdapterArtifactWriter writer) : IGitCheckpointCoordinator
{
    public async Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await inner.EvaluateAsync(request, cancellationToken);
        evidence.RecordCheckpoint(decision, null);
        await writer.WriteAsync(evidence, cancellationToken);
        return decision;
    }

    public async Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        var result = await inner.CommitAsync(request, cancellationToken);
        evidence.RecordCheckpoint(GitCheckpointPolicy.Evaluate(request), result);
        await writer.WriteAsync(evidence, cancellationToken);
        return result;
    }
}

public sealed class ProjectAdapterCheckpointRequestSource(
    IGitCheckpointRequestSource inner,
    ProjectAdapterEvidence evidence) : IGitCheckpointRequestSource
{
    public GitCheckpointRequest CreateForTask(RunRecord run) => inner.CreateForTask(run);

    public GitCheckpointRequest CreateForMilestone(string milestoneId, EngineMilestoneExecutionResult result)
    {
        var request = inner.CreateForMilestone(milestoneId, result);
        evidence.RecordApproval(request.HumanApprovalProvided, result.Status.ToString());
        return request;
    }
}
