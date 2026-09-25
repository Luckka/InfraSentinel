using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Architecture;

/// <summary>
/// InfraSentinel adapter for IAEngine's generic validation contract. The
/// architecture rules and their domain models remain owned by InfraSentinel.
/// </summary>
public sealed class ArchitectureDefenseValidationRunner(
    IArchitectureDefenseValidator validator,
    ArchitectureDecisionFixture fixture,
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
        var artifact = new ArchitectureDefenseArtifact(
            evaluation.DecisionId,
            evaluation.RulesEvaluated,
            evaluation.RulesPassed,
            evaluation.Findings,
            evaluation.Findings.Select(x => x.Severity).Distinct().ToArray(),
            evaluation.Justification,
            evaluation.Status,
            evaluation.RequiresHumanApproval);
        var output = JsonSerializer.Serialize(artifact, JsonOptions);

        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Architecture defense artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, output, cancellationToken);

        var process = new ProcessResult(
            "infrasentinel-architecture-defense",
            evaluation.Approved ? 0 : 1,
            output,
            "",
            TimeSpan.Zero);
        return [new ValidationResult(
            "ArchitectureDefense",
            true,
            process,
            evaluation.Approved ? ValidationStatus.Pass : ValidationStatus.Fail,
            evaluation.Approved ? null : evaluation.Justification)];
    }
}
