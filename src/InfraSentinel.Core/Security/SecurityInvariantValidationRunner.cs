using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Security;

public sealed class SecurityInvariantValidationRunner(
    ISecurityInvariantValidator validator,
    SecurityInvariantFixture fixture,
    string artifactPath,
    string milestoneId = "sentinel-security-invariant-gate",
    string taskId = "SECURITY-003") : IValidationRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evaluation = validator.Evaluate(fixture);
        var artifact = new SecurityInvariantArtifact(
            evaluation.ExecutionId,
            milestoneId,
            taskId,
            DateTimeOffset.UtcNow,
            evaluation.FixtureId,
            evaluation.FixtureVersion,
            evaluation.RulesEvaluated,
            evaluation.Findings,
            evaluation.Findings.Select(finding => finding.Severity).Distinct().ToArray(),
            fixture.Evidence ?? [],
            evaluation.Findings.Select(finding => finding.RemediationSuggestion).Distinct(StringComparer.Ordinal).ToArray(),
            evaluation.Status.ToString(),
            evaluation.RequiresHumanApproval ? "HumanRequired" : "Pending",
            evaluation.RequiresHumanApproval,
            "NotRequested");
        var output = JsonSerializer.Serialize(artifact, JsonOptions);
        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Security artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, output, cancellationToken);

        var process = new ProcessResult(
            "infrasentinel-security-invariant-validator",
            evaluation.Approved ? 0 : 1,
            output,
            "",
            TimeSpan.Zero);
        return [new ValidationResult(
            "SecurityInvariant",
            true,
            process,
            evaluation.Approved ? ValidationStatus.Pass : ValidationStatus.Fail,
            evaluation.Approved ? null : evaluation.Justification)];
    }
}
