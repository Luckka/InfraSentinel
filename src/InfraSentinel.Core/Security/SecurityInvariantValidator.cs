namespace InfraSentinel.Core.Security;

public interface ISecurityInvariantValidator
{
    SecurityInvariantEvaluation Evaluate(SecurityInvariantFixture fixture);
}

/// <summary>
/// Pure local security invariant validator. It inspects only the supplied
/// synthetic fixture and never invokes providers or infrastructure.
/// </summary>
public sealed class SecurityInvariantValidator : ISecurityInvariantValidator
{
    private static readonly string[] RuleIds =
    [
        "sensitive-resource-not-public",
        "sensitive-data-encrypted",
        "least-privilege",
        "plaintext-secret-prohibited",
        "critical-resource-audit-logging",
        "critical-dependency-owner-and-behavior",
        "critical-finding-human-approval"
    ];

    public SecurityInvariantEvaluation Evaluate(SecurityInvariantFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (string.IsNullOrWhiteSpace(fixture.FixtureId)) throw new ArgumentException("Fixture id is required.", nameof(fixture));
        if (string.IsNullOrWhiteSpace(fixture.Version)) throw new ArgumentException("Fixture version is required.", nameof(fixture));
        if (string.IsNullOrWhiteSpace(fixture.ExecutionId)) throw new ArgumentException("Execution id is required.", nameof(fixture));

        var findings = new List<SecurityInvariantFinding>();
        EvaluatePublicExposure(fixture, findings);
        EvaluateEncryption(fixture, findings);
        EvaluateLeastPrivilege(fixture, findings);
        EvaluateSecrets(fixture, findings);
        EvaluateAuditLogging(fixture, findings);
        EvaluateDependencyOwnership(fixture, findings);

        var critical = findings.Any(finding => finding.Severity == SecurityFindingSeverity.Critical);
        var requiresHuman = critical || findings.Count > 0;
        var passed = RuleIds.Where(rule => !findings.Any(finding => finding.RuleId == rule)).ToArray();
        if (critical) passed = passed.Where(rule => rule != "critical-finding-human-approval").ToArray();

        var status = requiresHuman ? SecurityInvariantStatus.HumanRequired : SecurityInvariantStatus.Approved;
        return new(
            fixture.ExecutionId,
            fixture.FixtureId,
            fixture.Version,
            RuleIds,
            passed,
            findings,
            status,
            requiresHuman,
            status == SecurityInvariantStatus.Approved
                ? "All deterministic security invariants passed."
                : $"Security review is required because {findings.Count} finding(s) remain.");
    }

    private static void EvaluatePublicExposure(SecurityInvariantFixture fixture, List<SecurityInvariantFinding> findings)
    {
        foreach (var resource in fixture.Resources ?? [])
        {
            if (resource.Classification is not (SecurityResourceClassification.Sensitive or SecurityResourceClassification.Critical)
                || resource.Exposure != SecurityExposure.Public) continue;
            var severity = resource.Classification == SecurityResourceClassification.Critical || resource.Critical
                ? SecurityFindingSeverity.Critical
                : SecurityFindingSeverity.High;
            findings.Add(Finding(
                "sensitive-resource-not-public",
                resource.ResourceId,
                severity,
                "Sensitive resource is publicly exposed.",
                ["classification=" + resource.Classification, "exposure=Public"],
                "Restrict the resource to private or explicitly authorized access."));
        }
    }

    private static void EvaluateEncryption(SecurityInvariantFixture fixture, List<SecurityInvariantFinding> findings)
    {
        foreach (var resource in fixture.Resources ?? [])
        {
            if (resource.Classification is not (SecurityResourceClassification.Sensitive or SecurityResourceClassification.Critical)
                || resource.EncryptionEnabled) continue;
            findings.Add(Finding(
                "sensitive-data-encrypted",
                resource.ResourceId,
                SecurityFindingSeverity.High,
                "Sensitive data has no explicit encryption control.",
                ["classification=" + resource.Classification, "encryption=false"],
                "Enable encryption and provide evidence of the selected control."));
        }
    }

    private static void EvaluateLeastPrivilege(SecurityInvariantFixture fixture, List<SecurityInvariantFinding> findings)
    {
        foreach (var identity in fixture.Identities ?? [])
        foreach (var permission in identity.Permissions ?? [])
        {
            if (!permission.Excessive) continue;
            findings.Add(Finding(
                "least-privilege",
                identity.IdentityId,
                SecurityFindingSeverity.High,
                "Identity has a permission broader than its declared purpose.",
                [permission.Evidence, "purpose=" + identity.DeclaredPurpose, "scope=" + permission.ResourceScope],
                "Remove the excessive permission or narrow its resource scope."));
        }
    }

    private static void EvaluateSecrets(SecurityInvariantFixture fixture, List<SecurityInvariantFinding> findings)
    {
        foreach (var secret in fixture.Secrets ?? [])
        {
            if (!secret.Plaintext) continue;
            findings.Add(Finding(
                "plaintext-secret-prohibited",
                secret.SecretId,
                SecurityFindingSeverity.Critical,
                "A secret is represented as plaintext in the fixture.",
                ["secret-id=" + secret.SecretId, "plaintext=true"],
                "Remove the plaintext value and reference a managed secret mechanism."));
        }
    }

    private static void EvaluateAuditLogging(SecurityInvariantFixture fixture, List<SecurityInvariantFinding> findings)
    {
        foreach (var resource in fixture.Resources ?? [])
        {
            if (!resource.Critical || resource.AuditLoggingEnabled) continue;
            findings.Add(Finding(
                "critical-resource-audit-logging",
                resource.ResourceId,
                SecurityFindingSeverity.High,
                "Critical resource has no explicit audit logging.",
                ["critical=true", "auditLogging=false"],
                "Enable audit logging and retain evidence of the configured destination."));
        }
    }

    private static void EvaluateDependencyOwnership(SecurityInvariantFixture fixture, List<SecurityInvariantFinding> findings)
    {
        foreach (var dependency in fixture.Dependencies ?? [])
        {
            if (!dependency.Critical || !string.IsNullOrWhiteSpace(dependency.Owner) && !string.IsNullOrWhiteSpace(dependency.OutageBehavior)) continue;
            findings.Add(Finding(
                "critical-dependency-owner-and-behavior",
                dependency.DependencyId,
                SecurityFindingSeverity.High,
                "Critical dependency has no complete owner and outage behavior.",
                ["critical=true", "owner=" + (dependency.Owner ?? "missing"), "outageBehavior=" + (dependency.OutageBehavior ?? "missing")],
                "Declare an accountable owner and the behavior during dependency unavailability."));
        }
    }

    private static SecurityInvariantFinding Finding(
        string ruleId,
        string resourceId,
        SecurityFindingSeverity severity,
        string title,
        IReadOnlyList<string> evidence,
        string remediation)
        => new(ruleId, resourceId, severity, title, title, evidence, remediation, SecurityFindingStatus.Failed, severity == SecurityFindingSeverity.Critical);
}
