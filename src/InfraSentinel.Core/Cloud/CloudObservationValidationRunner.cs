using IAEngine.Core.Git;
using InfraSentinel.Core.IaC;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Cloud;

public sealed class CloudObservationValidationRunner(
    ICloudObservationProvider provider,
    CloudObservationRequest request,
    NeutralIaCModel desired,
    string executionId,
    IacCloudSnapshotComparator comparator,
    CloudObservationEvidence evidence,
    ICloudEvidenceWriter writer,
    IMcpReadOnlyToolClient? mcpClient = null) : IValidationRunner
{
    public async Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maxAttempts = Math.Clamp(request.MaxAttempts, 1, 3);
        CloudObservationResult result = new(CloudCollectionStatus.Failed, null, [], "Observation was not attempted.", provider.SourceType == CloudObservationSourceType.Synthetic);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            result = await provider.GetSnapshotAsync(request, cancellationToken);
            if (result.Status == CloudCollectionStatus.Collected) break;
            if (attempt < maxAttempts) evidence.RecordRetry();
        }
        if (result.Status != CloudCollectionStatus.Collected || result.Snapshot is null)
        {
            evidence.RecordObservation(result, desired, mcpClient?.Calls);
            await writer.WriteAsync(evidence, cancellationToken);
            return [new ValidationResult("CloudObservation", true, new ProcessResult("infrasentinel-cloud-observation", 1, result.Reason, "", TimeSpan.Zero), ValidationStatus.Fail, result.Reason)];
        }
        var evaluation = comparator.Compare(executionId, desired, result.Snapshot);
        evidence.RecordObservation(result, desired, mcpClient?.Calls);
        evidence.RecordEvaluation(evaluation);
        await writer.WriteAsync(evidence, cancellationToken);
        var passed = evaluation.Approved;
        return [new ValidationResult("CloudObservation", true, new ProcessResult("infrasentinel-cloud-observation", passed ? 0 : 1, evaluation.Reason, "", TimeSpan.Zero), passed ? ValidationStatus.Pass : ValidationStatus.Fail, passed ? null : evaluation.Reason)];
    }
}
