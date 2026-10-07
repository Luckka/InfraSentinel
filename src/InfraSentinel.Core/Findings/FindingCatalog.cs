using System.Text.Json;
using System.Text.Json.Serialization;
using InfraSentinel.Core.Architecture;
using InfraSentinel.Core.Cloud;
using InfraSentinel.Core.IaC;
using InfraSentinel.Core.Security;

namespace InfraSentinel.Core.Findings;

public sealed record FindingCatalogEntry(
    string Source,
    string RuleId,
    string? ResourceId,
    string Severity,
    string Status,
    string Explanation,
    IReadOnlyList<string> Evidence,
    string? Remediation,
    bool RequiresHumanApproval,
    string DeterministicKey);

public sealed record FindingCatalogArtifact(
    string SchemaVersion,
    string ExecutionId,
    string ProjectId,
    string MilestoneId,
    IReadOnlyList<FindingCatalogEntry> Findings,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Severities,
    int FindingCount,
    int HumanApprovalCount,
    string Status,
    string Reason);

public sealed class FindingCatalog(
    string executionId,
    string projectId,
    string milestoneId)
{
    private readonly Dictionary<string, FindingCatalogEntry> entries = new(StringComparer.Ordinal);

    public void AddCloud(string source, IEnumerable<CloudFinding> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(findings);
        foreach (var finding in findings)
            Add(new(source, finding.RuleIdentifier, finding.Resource.ResourceId, finding.Severity.ToString(), finding.Status.ToString(), finding.Explanation,
                finding.Evidence.Select(item => $"{item.Source}:{item.Detail}").Order(StringComparer.Ordinal).ToArray(), finding.Remediation.Action,
                finding.RequiresHumanApproval, finding.DeterministicKey));
    }

    public void AddTerraform(string source, IEnumerable<IaCFinding> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(findings);
        foreach (var finding in findings)
            Add(new(source, finding.RuleId, finding.Resource, finding.Severity.ToString(), "Finding", finding.Explanation,
                finding.Evidence.Order(StringComparer.Ordinal).ToArray(), finding.RemediationGuidance, finding.RequiresHumanApproval,
                $"{source}|{finding.RuleId}|{finding.Resource}|{finding.File}|{finding.Line}"));
    }

    public void AddSecurity(string source, IEnumerable<SecurityInvariantFinding> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(findings);
        foreach (var finding in findings)
            Add(new(source, finding.RuleId, finding.ResourceId, finding.Severity.ToString(), finding.Status.ToString(), finding.Explanation,
                finding.Evidence.Order(StringComparer.Ordinal).ToArray(), finding.RemediationSuggestion, finding.RequiresHumanApproval,
                $"{source}|{finding.RuleId}|{finding.ResourceId}|{finding.Explanation}"));
    }

    public void AddArchitecture(string source, IEnumerable<ArchitectureDefenseFinding> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(findings);
        foreach (var finding in findings)
            Add(new(source, finding.RuleId, finding.DecisionId, finding.Severity.ToString(), "Finding", finding.Explanation, [], null,
                finding.RequiresHumanApproval, $"{source}|{finding.RuleId}|{finding.DecisionId}|{finding.Explanation}"));
    }

    public FindingCatalogArtifact BuildArtifact()
    {
        var findings = entries.Values
            .OrderByDescending(item => SeverityRank(item.Severity))
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.RuleId, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId, StringComparer.Ordinal)
            .ThenBy(item => item.DeterministicKey, StringComparer.Ordinal)
            .ToArray();
        var humanApprovalCount = findings.Count(item => item.RequiresHumanApproval);
        var status = humanApprovalCount > 0 ? "HumanRequired" : findings.Length > 0 ? "Finding" : "Approved";
        var reason = status == "Approved" ? "No findings were supplied." : "Consolidated findings require review before approval.";
        return new("1", executionId, projectId, milestoneId, findings,
            findings.Select(item => item.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            findings.Select(item => item.Severity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            findings.Length, humanApprovalCount, status, reason);
    }

    private void Add(FindingCatalogEntry entry) => entries.TryAdd(entry.DeterministicKey, entry);

    private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };
}

public sealed class FindingCatalogWriter(string artifactPath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(FindingCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Finding catalog path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(catalog.BuildArtifact(), Options), cancellationToken);
    }
}
