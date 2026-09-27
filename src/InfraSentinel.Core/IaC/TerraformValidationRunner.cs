using IAEngine.Core.Git;
using InfraSentinel.Core.Adapters;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.IaC;

public sealed class TerraformValidationRunner(
    IProjectAdapterSelector selector,
    ProjectAdapterRequest request,
    string executionId,
    TerraformStaticAnalysisValidator validator,
    TerraformAnalysisEvidence evidence,
    TerraformAnalysisArtifactWriter writer) : IValidationRunner
{
    public async Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = await selector.SelectAsync(request, cancellationToken);
        var analysis = adapter is null
            ? new ProjectAdapterAnalysis(ProjectAdapterStatus.Unsupported, "none", "0", "unknown", null, [], ["No unique project adapter supports this workspace."], [new("IAC-UNSUPPORTED", "High", "No unique adapter supports this workspace.", ["workspace"], false)], "No unique adapter supports this workspace.")
            : await adapter.AnalyzeAsync(request, cancellationToken);
        evidence.RecordAnalysis(analysis);
        var model = analysis.DomainModel as NeutralIaCModel;
        var evaluation = model is null
            ? new TerraformEvaluation(executionId, new NeutralIaCModel(request.ProjectId, "terraform", "1.0-subset", [], [], [], [], [], [], [], ["No neutral IaC model was produced."]), [], [],
                [new("IAC-MODEL-MISSING", "", IaCFindingSeverity.High, "The adapter did not produce a neutral IaC model.", ["adapter"], "", null, "Provide a supported Terraform fixture.", 1.0, null, false)], IaCAnalysisStatus.Invalid, false, "No neutral IaC model was produced.")
            : validator.Evaluate(executionId, model);
        if (analysis.Findings.Count > 0)
            evaluation = evaluation with { Findings = [.. evaluation.Findings, .. analysis.Findings.Select(finding => new IaCFinding(finding.RuleId, "", ParseSeverity(finding.Severity), finding.Explanation, finding.Evidence, finding.Evidence.FirstOrDefault() ?? "", null, "Resolve the adapter finding before approval.", 1.0, null, finding.RequiresHumanApproval))], Status = analysis.Status == ProjectAdapterStatus.Blocked && analysis.Findings.Any(finding => finding.RequiresHumanApproval) ? IaCAnalysisStatus.HumanRequired : evaluation.Status, RequiresHumanApproval = evaluation.RequiresHumanApproval || analysis.Findings.Any(finding => finding.RequiresHumanApproval) };
        evidence.RecordEvaluation(evaluation);
        await writer.WriteAsync(evidence, cancellationToken);
        var passed = evaluation.Approved;
        return [new ValidationResult("TerraformStaticAnalysis", true, new ProcessResult("infrasentinel-terraform-static-analysis", passed ? 0 : 1, evaluation.Justification, "", TimeSpan.Zero), passed ? ValidationStatus.Pass : ValidationStatus.Fail, passed ? null : evaluation.Justification)];
    }

    private static IaCFindingSeverity ParseSeverity(string severity)
        => Enum.TryParse<IaCFindingSeverity>(severity, true, out var parsed) ? parsed : IaCFindingSeverity.High;
}
