using System.Text.Json;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Validation;

public enum InfrastructureFindingSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record SyntheticInfrastructureResource(
    string? Id,
    string Kind,
    bool ExplicitlyInsecure = false,
    string? DependsOn = null);

public sealed record SyntheticInfrastructureFixture(
    IReadOnlyList<SyntheticInfrastructureResource> Resources,
    IReadOnlyList<string> RequiredPolicies,
    IReadOnlyList<string> ConfiguredPolicies);

public sealed record InfrastructureFinding(
    string RuleId,
    string? ResourceId,
    InfrastructureFindingSeverity Severity,
    string Explanation);

public sealed record InfrastructureValidationResult(
    bool Passed,
    IReadOnlyList<InfrastructureFinding> Findings);

public interface IInfrastructureValidator
{
    InfrastructureValidationResult Validate(SyntheticInfrastructureFixture fixture);
}

public sealed class DeterministicInfrastructureValidator : IInfrastructureValidator
{
    public InfrastructureValidationResult Validate(SyntheticInfrastructureFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var findings = new List<InfrastructureFinding>();
        var resources = fixture.Resources ?? [];
        var configuredPolicies = new HashSet<string>(fixture.ConfiguredPolicies ?? [], StringComparer.OrdinalIgnoreCase);
        var resourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            var resourceId = string.IsNullOrWhiteSpace(resource.Id) ? null : resource.Id.Trim();
            if (resourceId is null)
            {
                findings.Add(new(
                    "resource-identification-required",
                    null,
                    InfrastructureFindingSeverity.High,
                    $"Resource of kind '{resource.Kind}' has no required identification."));
            }
            else if (!resourceIds.Add(resourceId))
            {
                findings.Add(new(
                    "resource-identification-unique",
                    resourceId,
                    InfrastructureFindingSeverity.High,
                    "Resource identification must be unique within the fixture."));
            }

            if (resource.ExplicitlyInsecure)
            {
                findings.Add(new(
                    "explicit-insecure-configuration",
                    resourceId,
                    InfrastructureFindingSeverity.High,
                    "The fixture explicitly marks this resource configuration as insecure."));
            }
        }

        foreach (var resource in resources.Where(x => !string.IsNullOrWhiteSpace(x.DependsOn)))
        {
            if (!resourceIds.Contains(resource.DependsOn!))
            {
                findings.Add(new(
                    "dependency-must-exist",
                    string.IsNullOrWhiteSpace(resource.Id) ? null : resource.Id.Trim(),
                    InfrastructureFindingSeverity.High,
                    $"Dependency '{resource.DependsOn}' is not defined in the fixture."));
            }
        }

        foreach (var policy in fixture.RequiredPolicies ?? [])
        {
            if (!configuredPolicies.Contains(policy))
            {
                findings.Add(new(
                    "required-policy-present",
                    null,
                    InfrastructureFindingSeverity.High,
                    $"Required policy '{policy}' is not configured."));
            }
        }

        return new InfrastructureValidationResult(findings.Count == 0, findings);
    }
}

/// <summary>
/// Consumer adapter that exposes the domain validator to the generic Engine
/// validation stage without moving InfraSentinel rules into the Engine.
/// </summary>
public sealed class InfrastructureValidationRunner(
    IInfrastructureValidator validator,
    SyntheticInfrastructureFixture fixture) : IValidationRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var report = validator.Validate(fixture);
        var output = JsonSerializer.Serialize(report, JsonOptions);
        var process = new ProcessResult(
            "infrasentinel-local-validator",
            report.Passed ? 0 : 1,
            output,
            "",
            TimeSpan.Zero);
        return Task.FromResult<IReadOnlyList<ValidationResult>>([
            new("InfrastructureFixture", true, process,
                report.Passed ? ValidationStatus.Pass : ValidationStatus.Fail,
                report.Passed ? null : "Synthetic infrastructure fixture contains explainable findings.")
        ]);
    }
}
