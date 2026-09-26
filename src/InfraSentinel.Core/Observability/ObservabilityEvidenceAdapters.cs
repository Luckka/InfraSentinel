using IAEngine.Core.Git;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Observability;

public sealed class ObservabilityEvidenceValidationRunner(
    IValidationRunner inner,
    ObservabilityEvidenceAggregator aggregator,
    string taskId) : IValidationRunner
{
    public async Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = await inner.RunAsync(cancellationToken);
        var passed = results.Count > 0 && results.All(result => result.Passed);
        aggregator.RecordValidation(taskId, passed, passed ? "Validation passed." : "Validation failed: " + string.Join("; ", results.Select(result => result.Reason ?? result.Category)));
        return results;
    }

    public Task<IReadOnlyList<ValidationResult>> RunAsync(EngineeringProfile engineering, CancellationToken cancellationToken = default)
        => RunAsync(cancellationToken);
}

public sealed class ObservabilityEvidenceReviewAgent(
    IReviewAgent inner,
    ObservabilityEvidenceAggregator aggregator,
    string taskId) : IReviewAgent
{
    public async Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
    {
        var result = await inner.ReviewAsync(task, engineering, gitDiff, cancellationToken);
        aggregator.RecordReview(taskId, result.Decision == ReviewDecision.Pass, result.Summary);
        return result;
    }

    public async Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, string gitDiff, CancellationToken cancellationToken = default)
    {
        var result = await inner.ReviewAsync(task, engineering, validation, gitDiff, cancellationToken);
        aggregator.RecordReview(taskId, result.Decision == ReviewDecision.Pass, result.Summary);
        return result;
    }
}

public sealed class ObservabilityEvidenceCheckpointCoordinator(
    IGitCheckpointCoordinator inner,
    ObservabilityEvidenceAggregator aggregator,
    ObservabilityEvidenceWriter writer) : IGitCheckpointCoordinator
{
    private GitCheckpointDecision? lastDecision;

    public async Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await inner.EvaluateAsync(request, cancellationToken);
        lastDecision = decision;
        aggregator.RecordCheckpoint(request.TaskId, decision, null);
        await writer.WriteAsync(aggregator, cancellationToken);
        return decision;
    }

    public async Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        var result = await inner.CommitAsync(request, cancellationToken);
        var decision = lastDecision ?? GitCheckpointPolicy.Evaluate(request);
        aggregator.RecordCheckpoint(request.TaskId, decision, result);
        await writer.WriteAsync(aggregator, cancellationToken);
        return result;
    }
}

public sealed class ObservabilityEvidenceCheckpointRequestSource(
    IGitCheckpointRequestSource inner,
    ObservabilityEvidenceAggregator aggregator) : IGitCheckpointRequestSource
{
    public GitCheckpointRequest CreateForTask(RunRecord run)
        => inner.CreateForTask(run);

    public GitCheckpointRequest CreateForMilestone(string milestoneId, EngineMilestoneExecutionResult result)
    {
        var request = inner.CreateForMilestone(milestoneId, result);
        aggregator.RecordApproval(request.TaskId, result.Status == OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved, result.Status.ToString());
        return request;
    }
}
