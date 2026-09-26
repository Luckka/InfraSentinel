using IAEngine.Core.Git;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Adapters;

public sealed class ProjectAdapterValidationRunner(
    IProjectAdapterSelector selector,
    ProjectAdapterRequest request,
    ProjectAdapterEvidence evidence,
    ProjectAdapterArtifactWriter writer) : IValidationRunner
{
    public async Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = await selector.SelectAsync(request, cancellationToken);
        var analysis = adapter is null
            ? new ProjectAdapterAnalysis(ProjectAdapterStatus.Unsupported, "none", "0", "unknown", null, [], ["No unique adapter supports this workspace."],
                [new("ADAPTER-UNSUPPORTED", "High", "No unique project adapter was found.", ["workspace"], false)], "No unique adapter supports this workspace.")
            : await adapter.AnalyzeAsync(request, cancellationToken);
        evidence.RecordAnalysis(analysis);
        var passed = analysis.Status == ProjectAdapterStatus.Analyzed && analysis.Findings.Count == 0;
        evidence.RecordValidation("ProjectAdapter", passed, analysis.Reason);
        await writer.WriteAsync(evidence, cancellationToken);
        var output = string.Join("\n", analysis.Evidence);
        return [new ValidationResult("ProjectAdapter", true, new ProcessResult("infrasentinel-project-adapter", passed ? 0 : 1, output, "", TimeSpan.Zero), passed ? ValidationStatus.Pass : ValidationStatus.Fail, passed ? null : analysis.Reason)];
    }
}
