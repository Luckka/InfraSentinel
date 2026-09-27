using InfraSentinel.Core.IaC;

namespace InfraSentinel.Core.Cloud;

public sealed class IacCloudSnapshotComparator
{
    private static readonly string[] Rules =
    [
        "CLOUD-RESOURCE-MISSING", "CLOUD-RESOURCE-EXTRA", "CLOUD-PUBLIC-DRIFT", "CLOUD-ENCRYPTION-DRIFT",
        "CLOUD-LOGGING-DRIFT", "CLOUD-BACKUP-DRIFT", "CLOUD-OWNER-MISSING", "CLOUD-REGION-DRIFT",
        "CLOUD-ENVIRONMENT-DRIFT", "CLOUD-DEPENDENCY-DRIFT", "CLOUD-CRITICAL-OUT-OF-SCOPE", "CLOUD-UNKNOWN"
    ];

    public CloudObservationEvaluation Compare(string executionId, NeutralIaCModel desired, CloudInfrastructureSnapshot observed)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(observed);
        var findings = new List<CloudObservationFinding>();
        var desiredIds = desired.Resources.Select(resource => resource.LogicalId).ToHashSet(StringComparer.Ordinal);
        var observedIds = observed.Resources.Select(resource => resource.ResourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var resource in desired.Resources)
        {
            if (!observedIds.Contains(resource.LogicalId))
            {
                Add(findings, "CLOUD-RESOURCE-MISSING", resource.LogicalId, "declared", "absent", CloudFindingSeverity.High, "Declared resource is absent from the cloud snapshot.", resource.File, resource.Line, "Confirm scope or reconcile the resource.", false);
                continue;
            }
            var actual = observed.Resources.Single(item => item.ResourceId == resource.LogicalId);
            CompareField(findings, "CLOUD-PUBLIC-DRIFT", resource, resource.PublicExposure, actual.PublicExposure, "public exposure", CloudFindingSeverity.Critical);
            CompareField(findings, "CLOUD-ENCRYPTION-DRIFT", resource, resource.EncryptionConfigured, actual.EncryptionEnabled, "encryption", CloudFindingSeverity.High);
            CompareField(findings, "CLOUD-LOGGING-DRIFT", resource, resource.LoggingConfigured, actual.LoggingEnabled, "logging", CloudFindingSeverity.High);
            CompareField(findings, "CLOUD-BACKUP-DRIFT", resource, resource.BackupConfigured, actual.BackupEnabled, "backup", CloudFindingSeverity.High);
            if (!string.IsNullOrWhiteSpace(resource.Owner) && string.IsNullOrWhiteSpace(actual.Owner))
                Add(findings, "CLOUD-OWNER-MISSING", resource.LogicalId, resource.Owner!, "unknown", CloudFindingSeverity.High, "Observed resource has no owner evidence.", resource.File, resource.Line, "Establish resource ownership.", false);
            if (actual.Region != observed.Region)
                Add(findings, "CLOUD-REGION-DRIFT", resource.LogicalId, observed.Region, actual.Region, CloudFindingSeverity.High, "Observed resource region differs from the snapshot scope.", resource.File, resource.Line, "Confirm the intended region.", false);
            if (actual.Environment != observed.Environment)
                Add(findings, "CLOUD-ENVIRONMENT-DRIFT", resource.LogicalId, observed.Environment, actual.Environment, CloudFindingSeverity.High, "Observed resource environment differs from the snapshot scope.", resource.File, resource.Line, "Confirm environment identity.", false);
            if (resource.Dependencies.Count > 0 && !resource.Dependencies.All(actual.Dependencies.Contains))
                Add(findings, "CLOUD-DEPENDENCY-DRIFT", resource.LogicalId, string.Join(',', resource.Dependencies), string.Join(',', actual.Dependencies), CloudFindingSeverity.High, "Observed dependencies do not prove all declared dependencies.", resource.File, resource.Line, "Reconcile critical dependencies.", false);
        }
        foreach (var extra in observed.Resources.Where(resource => !desiredIds.Contains(resource.ResourceId)))
            findings.Add(new("CLOUD-RESOURCE-EXTRA", extra.ResourceId, "not-declared", "observed", CloudFindingSeverity.Medium, "Observed resource is not declared in the IaC model.", extra.Evidence, null, "Review ownership and declaration scope.", false));
        if (observed.Limitations.Count > 0)
            findings.Add(new("CLOUD-UNKNOWN", "snapshot", "complete evidence", string.Join(';', observed.Limitations), CloudFindingSeverity.Medium, "The snapshot has limitations that prevent a complete comparison.", observed.Limitations, string.Join(';', observed.Limitations), "Collect sufficient read-only evidence before approval.", true));
        var rulesPassed = Rules.Except(findings.Select(finding => finding.RuleId), StringComparer.Ordinal).ToArray();
        var status = findings.Any(finding => finding.RequiresHumanApproval || finding.Severity == CloudFindingSeverity.Critical)
            ? CloudObservationStatus.HumanRequired
            : findings.Count == 0 ? CloudObservationStatus.Approved : CloudObservationStatus.Unknown;
        return new(status, findings, Rules, rulesPassed, observed.Limitations, findings.Count == 0 ? "IaC and cloud snapshot agree." : "IaC and cloud snapshot require review.");
    }

    private static void CompareField(List<CloudObservationFinding> findings, string rule, IaCResource resource, bool? desired, bool? observed, string field, CloudFindingSeverity severity)
    {
        if (desired is null || observed is null)
        {
            if (desired == true && observed is null)
                Add(findings, "CLOUD-UNKNOWN", resource.LogicalId, desired.Value.ToString(), "unknown", CloudFindingSeverity.Medium, $"Observed {field} evidence is unknown.", resource.File, resource.Line, $"Collect {field} evidence.", true);
            return;
        }
        if (desired != observed)
            Add(findings, rule, resource.LogicalId, desired.Value.ToString(), observed.Value.ToString(), severity, $"Observed {field} differs from the desired IaC state.", resource.File, resource.Line, $"Reconcile {field} configuration.", severity == CloudFindingSeverity.Critical);
    }

    private static void Add(List<CloudObservationFinding> findings, string rule, string resource, string desired, string observed, CloudFindingSeverity severity, string explanation, string file, int line, string remediation, bool human)
        => findings.Add(new(rule, resource, desired, observed, severity, explanation, [$"{file}:{line}"], null, remediation, human));
}
