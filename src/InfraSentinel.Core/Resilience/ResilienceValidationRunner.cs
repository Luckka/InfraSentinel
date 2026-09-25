using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Resilience;

/// <summary>
/// Thin adapter from InfraSentinel's resilience domain to IAEngine's generic
/// validation contract. IAEngine owns workflow state; InfraSentinel owns rules.
/// </summary>
public sealed class ResilienceValidationRunner(
    IResilienceContractValidator validator,
    ResilienceContractFixture fixture,
    string artifactPath) : IValidationRunner
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
        var artifact = new ResilienceContractArtifact(
            evaluation.ExecutionId,
            evaluation.FixtureId,
            evaluation.FixtureVersion,
            evaluation.ComponentsEvaluated,
            evaluation.RulesEvaluated,
            evaluation.RulesPassed,
            evaluation.Findings,
            evaluation.Findings.Select(finding => (ResilienceFindingSeverity?)finding.Severity)
                .Concat([evaluation.HighestSeverity])
                .Where(severity => severity is not null)
                .Select(severity => severity!.Value)
                .Distinct()
                .ToArray(),
            evaluation.Evidence,
            evaluation.Findings.Select(finding => finding.Recommendation).Distinct(StringComparer.Ordinal).ToArray(),
            evaluation.Status,
            evaluation.RequiresHumanApproval,
            evaluation.Justification);
        var output = JsonSerializer.Serialize(artifact, JsonOptions);

        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Resilience artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, output, cancellationToken);

        var process = new ProcessResult(
            "infrasentinel-resilience-contract",
            evaluation.Approved ? 0 : 1,
            output,
            "",
            TimeSpan.Zero);
        return [new ValidationResult(
            "ResilienceContract",
            true,
            process,
            evaluation.Approved ? ValidationStatus.Pass : ValidationStatus.Fail,
            evaluation.Approved ? null : evaluation.Justification)];
    }
}
