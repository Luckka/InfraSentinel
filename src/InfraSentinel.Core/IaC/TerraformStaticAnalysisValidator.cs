namespace InfraSentinel.Core.IaC;

public sealed class TerraformStaticAnalysisValidator
{
    private static readonly string[] Rules =
    [
        "IAC-PUBLIC-SENSITIVE",
        "IAC-ENCRYPTION-REQUIRED",
        "IAC-SENSITIVE-PORT",
        "IAC-IAM-LEAST-PRIVILEGE",
        "IAC-LOGGING-REQUIRED",
        "IAC-BACKUP-RETENTION",
        "IAC-CRITICAL-OWNER",
        "IAC-CRITICAL-DEPENDENCY",
        "IAC-PLAINTEXT-SECRET",
        "IAC-PRODUCTION-IDENTITY"
    ];

    public TerraformEvaluation Evaluate(string executionId, NeutralIaCModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var findings = new List<IaCFinding>();
        foreach (var resource in model.Resources)
        {
            if (resource.PublicExposure && IsSensitive(resource))
                Add(findings, "IAC-PUBLIC-SENSITIVE", resource, IaCFindingSeverity.Critical, "Sensitive infrastructure is publicly exposed.", "Restrict exposure to an explicit private boundary.");
            if (IsSensitive(resource) && resource.EncryptionConfigured != true)
                Add(findings, "IAC-ENCRYPTION-REQUIRED", resource, IaCFindingSeverity.High, "Sensitive infrastructure has no explicit encryption configuration.", "Declare encryption enabled and provide local evidence.");
            if (IsSecurityGroup(resource) && resource.PublicExposure && resource.Ports.Any(port => port is 22 or 3389))
                Add(findings, "IAC-SENSITIVE-PORT", resource, IaCFindingSeverity.Critical, "A sensitive administrative port is open to 0.0.0.0/0.", "Restrict the source range and use a controlled access path.");
            if (IsIam(resource) && resource.Permissions.Any(permission => permission.Contains('*', StringComparison.Ordinal)))
                Add(findings, "IAC-IAM-LEAST-PRIVILEGE", resource, IaCFindingSeverity.Critical, "IAM permissions contain a wildcard action or scope.", "Replace wildcard permissions with the minimum declared actions and resources.");
            if (resource.Critical && resource.LoggingConfigured != true)
                Add(findings, "IAC-LOGGING-REQUIRED", resource, IaCFindingSeverity.High, "Critical resource has no explicit logging configuration.", "Enable audit logging and identify its evidence destination.");
            if (resource.Critical && resource.BackupConfigured != true)
                Add(findings, "IAC-BACKUP-RETENTION", resource, IaCFindingSeverity.High, "Critical resource has no explicit backup or retention configuration.", "Declare a bounded backup and retention strategy.");
            if (resource.Critical && string.IsNullOrWhiteSpace(resource.Owner))
                Add(findings, "IAC-CRITICAL-OWNER", resource, IaCFindingSeverity.High, "Critical resource has no owner.", "Declare an accountable owner for the resource.");
            if (resource.Critical && resource.Dependencies.Any() && resource.Dependencies.Any(dependency => dependency.StartsWith("unknown", StringComparison.OrdinalIgnoreCase)))
                Add(findings, "IAC-CRITICAL-DEPENDENCY", resource, IaCFindingSeverity.High, "Critical resource contains an unresolved dependency.", "Declare dependency ownership and behavior explicitly.");
            if (resource.PlaintextSecret)
                Add(findings, "IAC-PLAINTEXT-SECRET", resource, IaCFindingSeverity.Critical, "A secret-like Terraform attribute contains a plaintext literal.", "Remove the literal and reference an approved secret boundary.");
            if (resource.Critical && string.Equals(resource.Environment, "production", StringComparison.OrdinalIgnoreCase) == false)
                Add(findings, "IAC-PRODUCTION-IDENTITY", resource, IaCFindingSeverity.Medium, "Critical resource is not explicitly identified with the production environment when required.", "Declare the intended environment explicitly.");
        }

        var passed = Rules.Except(findings.Select(finding => finding.RuleId), StringComparer.Ordinal).ToArray();
        var requiresHuman = findings.Any(finding => finding.RequiresHumanApproval);
        var status = requiresHuman ? IaCAnalysisStatus.HumanRequired : IaCAnalysisStatus.Analyzed;
        return new(executionId, model, Rules, passed, findings, status, requiresHuman,
            findings.Count == 0 ? "All Terraform static-analysis rules passed." : "Terraform static analysis found declared infrastructure risks.");
    }

    private static void Add(List<IaCFinding> findings, string ruleId, IaCResource resource, IaCFindingSeverity severity, string explanation, string remediation)
        => findings.Add(new(ruleId, resource.LogicalId, severity, explanation, resource.Evidence, resource.File, resource.Line, remediation, 0.95, resource.Limitations.FirstOrDefault(), severity == IaCFindingSeverity.Critical));

    private static bool IsSensitive(IaCResource resource)
        => resource.Critical || resource.ResourceType.Contains("bucket", StringComparison.OrdinalIgnoreCase)
            || resource.ResourceType.Contains("database", StringComparison.OrdinalIgnoreCase)
            || resource.ResourceType.Contains("storage", StringComparison.OrdinalIgnoreCase)
            || resource.ResourceType.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static bool IsSecurityGroup(IaCResource resource) => resource.ResourceType.Contains("security_group", StringComparison.OrdinalIgnoreCase);
    private static bool IsIam(IaCResource resource) => resource.ResourceType.Contains("iam", StringComparison.OrdinalIgnoreCase) || resource.ResourceType.Contains("policy", StringComparison.OrdinalIgnoreCase);
}
