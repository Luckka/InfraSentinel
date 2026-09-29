namespace InfraSentinel.Core.Cloud;

public enum CloudFindingEvaluationStatus { Pass, Finding, Unknown, NotApplicable, Blocked }
public enum CloudFindingsGateStatus { Approved, Finding, Unknown, HumanRequired, Blocked }
public enum CloudFindingConfidence { Low, Medium, High }

public sealed record CloudFindingEvidence(string Source, string Detail);

public sealed record CloudAffectedResource(string ResourceId, string ResourceType, string Region);

public sealed record CloudRemediationRecommendation(string Action, bool AutomaticExecutionAllowed = false);

public sealed record CloudRuleEvaluation(
    string RuleIdentifier,
    CloudFindingEvaluationStatus Status,
    CloudFindingSeverity Severity,
    CloudAffectedResource? Resource,
    IReadOnlyList<CloudFindingEvidence> Evidence,
    string? Limitation,
    string Explanation);

public sealed record CloudFinding(
    string RuleIdentifier,
    CloudAffectedResource Resource,
    CloudFindingSeverity Severity,
    string Title,
    string Explanation,
    IReadOnlyList<CloudFindingEvidence> Evidence,
    string? Limitation,
    CloudRemediationRecommendation Remediation,
    CloudFindingConfidence Confidence,
    CloudFindingEvaluationStatus Status,
    string DeterministicKey,
    bool RequiresHumanApproval);

public sealed record CloudFindingEvaluationResult(
    CloudFindingsGateStatus Status,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<CloudRuleEvaluation> RuleEvaluations,
    IReadOnlyList<CloudFinding> Findings,
    IReadOnlyList<string> Limitations,
    string Reason)
{
    public bool Approved => Status == CloudFindingsGateStatus.Approved;
}

public sealed class CloudFindingsGate(IReadOnlySet<string> authorizedRegions)
{
    private static readonly string[] SecurityRules =
    [
        "SECURITY-PUBLIC-EXPOSURE", "SECURITY-ADMIN-PORT", "SECURITY-ENCRYPTION",
        "SECURITY-BUCKET-PUBLIC-BLOCK", "SECURITY-LEAST-PRIVILEGE", "SECURITY-CRITICAL-OWNER",
        "SECURITY-PRODUCTION-IDENTITY", "SECURITY-REGION-ALLOWLIST", "SECURITY-CLASSIFICATION"
    ];
    private static readonly string[] ResilienceRules =
    [
        "RESILIENCE-RDS-MULTI-AZ", "RESILIENCE-BACKUP", "RESILIENCE-RETENTION",
        "RESILIENCE-UNAVAILABILITY-BEHAVIOR", "RESILIENCE-REDUNDANCY", "RESILIENCE-DEPENDENCY-OWNER",
        "RESILIENCE-DEPENDENCY-OBSERVABILITY", "RESILIENCE-RECOVERY-EVIDENCE", "RESILIENCE-SINGLE-POINT-OF-FAILURE"
    ];
    private static readonly string[] ObservabilityRules =
    [
        "OBSERVABILITY-LOGGING", "OBSERVABILITY-METRICS", "OBSERVABILITY-ALARMS",
        "OBSERVABILITY-TRACEABILITY", "OBSERVABILITY-OPERATIONAL-EVIDENCE", "OBSERVABILITY-CRITICAL-MONITORING"
    ];
    private static readonly string[] CostRules =
    [
        "COST-NAT-GATEWAY", "COST-ELASTIC-IP", "COST-RDS", "COST-LOAD-BALANCER",
        "COST-TAGS", "COST-PAID-SERVICE", "COST-REGION", "COST-OWNER", "COST-PURPOSE"
    ];
    private static readonly string[] Rules = SecurityRules.Concat(ResilienceRules).Concat(ObservabilityRules).Concat(CostRules).ToArray();

    public CloudFindingEvaluationResult Evaluate(CloudInfrastructureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var evaluations = new List<CloudRuleEvaluation>();
        var findings = new List<CloudFinding>();
        var limitations = snapshot.Limitations.Order(StringComparer.Ordinal).ToList();
        if (snapshot.CollectionStatus != CloudCollectionStatus.Collected)
        {
            var limitation = $"snapshot-status:{snapshot.CollectionStatus}";
            var evidence = new[] { new CloudFindingEvidence("snapshot", $"collectionStatus={snapshot.CollectionStatus}") };
            limitations.Add(limitation);
            evaluations.Add(new("SNAPSHOT-STATUS", CloudFindingEvaluationStatus.Blocked, CloudFindingSeverity.Critical, null, evidence, limitation, "Snapshot collection did not complete."));
            return new(CloudFindingsGateStatus.Blocked, Rules, [], evaluations, [], limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), "Snapshot collection is not complete; findings gate is blocked.");
        }
        if (snapshot.Resources.Count == 0)
            AddGlobalUnknown("SNAPSHOT-NONEMPTY", "Snapshot contains no resources; absence of resources is not proof of complete observation.", ["resources=0"], evaluations, findings, limitations);
        if (snapshot.Limitations.Count > 0)
            AddGlobalUnknown("SNAPSHOT-LIMITATIONS", "Snapshot limitations prevent complete safety conclusions.", snapshot.Limitations, evaluations, findings, limitations);

        foreach (var resource in snapshot.Resources.OrderBy(item => item.ResourceType, StringComparer.Ordinal).ThenBy(item => item.ResourceId, StringComparer.Ordinal))
        {
            var affected = new CloudAffectedResource(resource.ResourceId, resource.ResourceType, resource.Region);
            EvaluateSecurity(snapshot, resource, affected, evaluations, findings, limitations);
            EvaluateResilience(resource, affected, evaluations, findings, limitations);
            EvaluateObservability(resource, affected, evaluations, findings, limitations);
            EvaluateCost(resource, affected, evaluations, findings, limitations);
        }

        var orderedFindings = findings.OrderByDescending(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.RuleIdentifier, StringComparer.Ordinal)
            .ThenBy(finding => finding.Resource.ResourceType, StringComparer.Ordinal)
            .ThenBy(finding => finding.Resource.ResourceId, StringComparer.Ordinal)
            .ThenBy(finding => finding.Explanation, StringComparer.Ordinal)
            .ToArray();
        var orderedEvaluations = evaluations.OrderBy(item => item.RuleIdentifier, StringComparer.Ordinal)
            .ThenBy(item => item.Resource?.ResourceType, StringComparer.Ordinal)
            .ThenBy(item => item.Resource?.ResourceId, StringComparer.Ordinal)
            .ToArray();
        var passed = Rules.Except(orderedEvaluations.Where(item => item.Status is not CloudFindingEvaluationStatus.Pass and not CloudFindingEvaluationStatus.NotApplicable).Select(item => item.RuleIdentifier), StringComparer.Ordinal).ToArray();
        var status = orderedEvaluations.Any(item => item.Status == CloudFindingEvaluationStatus.Blocked)
            ? CloudFindingsGateStatus.Blocked
            : orderedFindings.Any(item => item.RequiresHumanApproval || item.Severity is CloudFindingSeverity.Critical or CloudFindingSeverity.High)
                ? CloudFindingsGateStatus.HumanRequired
                : orderedEvaluations.Any(item => item.Status == CloudFindingEvaluationStatus.Unknown)
                    ? CloudFindingsGateStatus.Unknown
                    : orderedFindings.Length > 0 ? CloudFindingsGateStatus.Finding : CloudFindingsGateStatus.Approved;
        return new(status, Rules, passed, orderedEvaluations, orderedFindings, limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), status == CloudFindingsGateStatus.Approved ? "All cloud findings rules passed." : "Cloud findings require review or better evidence.");
    }

    private void EvaluateSecurity(CloudInfrastructureSnapshot snapshot, CloudResourceSnapshot resource, CloudAffectedResource affected, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        AddBoolean("SECURITY-PUBLIC-EXPOSURE", resource, affected, resource.PublicExposure, true, CloudFindingSeverity.High, "Public exposure is present.", "Restrict public exposure to explicitly authorized paths.", evaluations, findings, limitations, critical: true);
        AddPropertyBoolean("SECURITY-ADMIN-PORT", resource, affected, "adminPortOpen", CloudFindingSeverity.Critical, "An administrative port is publicly open.", "Restrict administrative ports to approved network sources.", evaluations, findings, limitations, critical: true);
        AddBoolean("SECURITY-ENCRYPTION", resource, affected, resource.EncryptionEnabled, false, CloudFindingSeverity.High, "Encryption is explicitly absent.", "Enable encryption and retain configuration evidence.", evaluations, findings, limitations);
        if (resource.ResourceType.Equals("s3-bucket", StringComparison.OrdinalIgnoreCase))
            AddBoolean("SECURITY-BUCKET-PUBLIC-BLOCK", resource, affected, resource.PublicExposure, true, CloudFindingSeverity.Critical, "The bucket does not have sufficient public-access-block evidence.", "Enable and verify public access blocking.", evaluations, findings, limitations, critical: true);
        AddPropertyBoolean("SECURITY-LEAST-PRIVILEGE", resource, affected, "leastPrivilege", CloudFindingSeverity.High, "Least privilege is explicitly absent.", "Narrow permissions to the declared purpose and resource scope.", evaluations, findings, limitations, invert: true);
        if (IsCritical(resource))
            AddPresence("SECURITY-CRITICAL-OWNER", resource, affected, Owner(resource), CloudFindingSeverity.High, "Critical resource has no owner evidence.", "Associate an accountable owner.", evaluations, findings, limitations);
        if (IsProduction(snapshot, resource))
            AddPresence("SECURITY-PRODUCTION-IDENTITY", resource, affected, Property(resource, "environment"), CloudFindingSeverity.Medium, "Production resource has no explicit environment identity.", "Declare the production environment and ownership.", evaluations, findings, limitations);
        AddRegion("SECURITY-REGION-ALLOWLIST", resource, affected, evaluations, findings, limitations);
        var classification = Property(resource, "classification");
        if (string.IsNullOrWhiteSpace(classification) || classification.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            AddUnknown("SECURITY-CLASSIFICATION", resource, affected, "Resource classification is unknown.", "Classify the resource before approving its posture.", evaluations, findings, limitations);
        else AddPass("SECURITY-CLASSIFICATION", affected, evaluations);
    }

    private static void EvaluateResilience(CloudResourceSnapshot resource, CloudAffectedResource affected, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        if (resource.ResourceType.Equals("rds-instance", StringComparison.OrdinalIgnoreCase))
            AddPropertyBoolean("RESILIENCE-RDS-MULTI-AZ", resource, affected, "multiAz", CloudFindingSeverity.High, "RDS Multi-AZ is explicitly disabled.", "Enable Multi-AZ or document an approved availability design.", evaluations, findings, limitations, invert: true);
        else AddNotApplicable("RESILIENCE-RDS-MULTI-AZ", affected, evaluations);
        AddBoolean("RESILIENCE-BACKUP", resource, affected, resource.BackupEnabled, false, CloudFindingSeverity.High, "Backup is explicitly absent.", "Enable backup and verify retention.", evaluations, findings, limitations);
        var retention = PropertyInt(resource, "backupRetentionDays");
        if (retention is null) AddUnknown("RESILIENCE-RETENTION", resource, affected, "Backup retention is unknown.", "Collect retention evidence and require an approved minimum.", evaluations, findings, limitations);
        else if (retention < 7) AddFinding("RESILIENCE-RETENTION", affected, CloudFindingSeverity.High, "Backup retention is below seven days.", "Increase retention to the approved resilience baseline.", [Evidence(resource, $"backupRetentionDays={retention}")], evaluations, findings);
        else AddPass("RESILIENCE-RETENTION", affected, evaluations);
        AddPropertyPresence("RESILIENCE-UNAVAILABILITY-BEHAVIOR", resource, affected, "availabilityBehavior", CloudFindingSeverity.Medium, "Behavior during unavailability is unknown.", "Document fallback, timeout, pending, or failover behavior.", evaluations, findings, limitations);
        AddPropertyBoolean("RESILIENCE-REDUNDANCY", resource, affected, "redundant", CloudFindingSeverity.High, "Critical resource redundancy is explicitly absent.", "Provide an approved redundant design or documented exception.", evaluations, findings, limitations, invert: true);
        if (resource.Dependencies.Count == 0) AddNotApplicable("RESILIENCE-DEPENDENCY-OWNER", affected, evaluations);
        else AddPresence("RESILIENCE-DEPENDENCY-OWNER", resource, affected, Owner(resource), CloudFindingSeverity.High, "Critical dependency has no owner evidence.", "Assign an owner to each critical dependency.", evaluations, findings, limitations);
        if (resource.Dependencies.Count == 0) AddNotApplicable("RESILIENCE-DEPENDENCY-OBSERVABILITY", affected, evaluations);
        else AddPropertyPresence("RESILIENCE-DEPENDENCY-OBSERVABILITY", resource, affected, "dependencyObservability", CloudFindingSeverity.High, "Critical dependency observability is unknown.", "Provide logs, metrics, traces, or alarms for the dependency.", evaluations, findings, limitations);
        AddPropertyPresence("RESILIENCE-RECOVERY-EVIDENCE", resource, affected, "recoveryEvidence", CloudFindingSeverity.Medium, "Recovery evidence is missing.", "Record a tested recovery path and its evidence.", evaluations, findings, limitations);
        AddPropertyBoolean("RESILIENCE-SINGLE-POINT-OF-FAILURE", resource, affected, "singlePointOfFailure", CloudFindingSeverity.High, "A potential single point of failure is identified.", "Remove the single point of failure or document an approved exception.", evaluations, findings, limitations, invert: false);
    }

    private static void EvaluateObservability(CloudResourceSnapshot resource, CloudAffectedResource affected, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        AddBoolean("OBSERVABILITY-LOGGING", resource, affected, resource.LoggingEnabled, false, CloudFindingSeverity.High, "Logging is explicitly absent.", "Enable logging and retain operational evidence.", evaluations, findings, limitations);
        AddPropertyBoolean("OBSERVABILITY-METRICS", resource, affected, "metrics", CloudFindingSeverity.Medium, "Metrics evidence is absent.", "Enable service metrics and define useful measurements.", evaluations, findings, limitations, invert: true);
        AddPropertyBoolean("OBSERVABILITY-ALARMS", resource, affected, "alarms", CloudFindingSeverity.Medium, "Alarm evidence is absent.", "Create and review alarms for the resource.", evaluations, findings, limitations, invert: true);
        AddPropertyPresence("OBSERVABILITY-TRACEABILITY", resource, affected, "traceability", CloudFindingSeverity.Low, "Traceability evidence is unknown.", "Provide correlation or trace evidence where applicable.", evaluations, findings, limitations);
        AddPropertyPresence("OBSERVABILITY-OPERATIONAL-EVIDENCE", resource, affected, "operationalEvidence", CloudFindingSeverity.Medium, "Operational evidence is missing.", "Record runbook, dashboard, or on-call evidence.", evaluations, findings, limitations);
        if (IsCritical(resource)) AddPropertyPresence("OBSERVABILITY-CRITICAL-MONITORING", resource, affected, "monitoring", CloudFindingSeverity.High, "Critical resource monitoring is unknown.", "Identify monitoring coverage and an accountable response path.", evaluations, findings, limitations);
        else AddNotApplicable("OBSERVABILITY-CRITICAL-MONITORING", affected, evaluations);
    }

    private void EvaluateCost(CloudResourceSnapshot resource, CloudAffectedResource affected, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        if (resource.ResourceType.Equals("nat-gateway", StringComparison.OrdinalIgnoreCase)) AddFinding("COST-NAT-GATEWAY", affected, CloudFindingSeverity.Medium, "NAT Gateway is potentially billable.", "Review whether the NAT Gateway is necessary and scoped.", [Evidence(resource, "resourceType=nat-gateway")], evaluations, findings);
        else AddNotApplicable("COST-NAT-GATEWAY", affected, evaluations);
        if (resource.ResourceType.Equals("elastic-ip", StringComparison.OrdinalIgnoreCase)) AddPropertyBoolean("COST-ELASTIC-IP", resource, affected, "associated", CloudFindingSeverity.Medium, "Elastic IP is not associated.", "Release or associate the address after human review.", evaluations, findings, limitations, invert: true);
        else AddNotApplicable("COST-ELASTIC-IP", affected, evaluations);
        if (resource.ResourceType.Equals("rds-instance", StringComparison.OrdinalIgnoreCase)) AddFinding("COST-RDS", affected, CloudFindingSeverity.Low, "RDS is potentially billable.", "Review engine, size, retention, and lab purpose.", [Evidence(resource, "resourceType=rds-instance")], evaluations, findings);
        else AddNotApplicable("COST-RDS", affected, evaluations);
        if (resource.ResourceType.Contains("load-balancer", StringComparison.OrdinalIgnoreCase)) AddFinding("COST-LOAD-BALANCER", affected, CloudFindingSeverity.Medium, "Load Balancer is potentially billable.", "Review whether the load balancer is required.", [Evidence(resource, $"resourceType={resource.ResourceType}")], evaluations, findings);
        else AddNotApplicable("COST-LOAD-BALANCER", affected, evaluations);
        AddPropertyBoolean("COST-TAGS", resource, affected, "tagsPresent", CloudFindingSeverity.Low, "Cost allocation tags are not evidenced.", "Apply the approved Project, Environment, ManagedBy, and CostCenter tags.", evaluations, findings, limitations, invert: true);
        if (new[] { "nat-gateway", "rds-instance", "lambda-function", "ecs-cluster", "eks-cluster", "load-balancer" }.Any(type => resource.ResourceType.Contains(type, StringComparison.OrdinalIgnoreCase))) AddFinding("COST-PAID-SERVICE", affected, CloudFindingSeverity.Low, "Resource belongs to a potentially paid service.", "Review service usage and lab purpose before approval.", [Evidence(resource, $"resourceType={resource.ResourceType}")], evaluations, findings);
        else AddNotApplicable("COST-PAID-SERVICE", affected, evaluations);
        AddRegion("COST-REGION", resource, affected, evaluations, findings, limitations);
        AddPresence("COST-OWNER", resource, affected, Owner(resource), CloudFindingSeverity.Medium, "Billable resource has no owner evidence.", "Associate an accountable owner before continuing usage.", evaluations, findings, limitations);
        AddPropertyPresence("COST-PURPOSE", resource, affected, "purpose", CloudFindingSeverity.Medium, "Workload purpose is unknown.", "Document the workload purpose and expected lifetime.", evaluations, findings, limitations);
    }

    private void AddRegion(string rule, CloudResourceSnapshot resource, CloudAffectedResource affected, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        if (authorizedRegions.Contains(resource.Region)) AddPass(rule, affected, evaluations);
        else AddFinding(rule, affected, CloudFindingSeverity.High, "Resource is outside the authorized region allowlist.", "Restrict observation and workload placement to approved regions.", [Evidence(resource, $"region={resource.Region}")], evaluations, findings);
    }

    private static void AddBoolean(string rule, CloudResourceSnapshot resource, CloudAffectedResource affected, bool? value, bool findingValue, CloudFindingSeverity severity, string title, string recommendation, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations, bool critical = false)
    {
        if (value is null) AddUnknown(rule, resource, affected, $"{title} evidence is unknown.", recommendation, evaluations, findings, limitations);
        else if (value == findingValue) AddFinding(rule, affected, critical ? CloudFindingSeverity.Critical : severity, title, recommendation, [Evidence(resource, $"observed={value}")], evaluations, findings, critical);
        else AddPass(rule, affected, evaluations);
    }

    private static void AddPropertyBoolean(string rule, CloudResourceSnapshot resource, CloudAffectedResource affected, string key, CloudFindingSeverity severity, string title, string recommendation, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations, bool critical = false, bool invert = false)
    {
        var value = PropertyBool(resource, key);
        if (value is null) AddUnknown(rule, resource, affected, $"{title} evidence is unknown.", recommendation, evaluations, findings, limitations);
        else if (value == !invert) AddFinding(rule, affected, critical ? CloudFindingSeverity.Critical : severity, title, recommendation, [Evidence(resource, $"{key}={value}")], evaluations, findings, critical);
        else AddPass(rule, affected, evaluations);
    }

    private static void AddPropertyPresence(string rule, CloudResourceSnapshot resource, CloudAffectedResource affected, string key, CloudFindingSeverity severity, string title, string recommendation, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        if (string.IsNullOrWhiteSpace(Property(resource, key))) AddUnknown(rule, resource, affected, title, recommendation, evaluations, findings, limitations);
        else AddPass(rule, affected, evaluations);
    }

    private static void AddPresence(string rule, CloudResourceSnapshot resource, CloudAffectedResource affected, string? value, CloudFindingSeverity severity, string title, string recommendation, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        if (string.IsNullOrWhiteSpace(value)) AddUnknown(rule, resource, affected, title, recommendation, evaluations, findings, limitations);
        else AddPass(rule, affected, evaluations);
    }

    private static void AddFinding(string rule, CloudAffectedResource resource, CloudFindingSeverity severity, string title, string recommendation, IReadOnlyList<CloudFindingEvidence> evidence, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, bool critical = false)
    {
        var confidence = evidence.Count > 0 ? CloudFindingConfidence.High : CloudFindingConfidence.Medium;
        var finding = new CloudFinding(rule, resource, severity, title, title, evidence, null, new(recommendation), confidence, CloudFindingEvaluationStatus.Finding, $"{rule}|{resource.ResourceType}|{resource.ResourceId}|{title}", critical || severity == CloudFindingSeverity.Critical || severity == CloudFindingSeverity.High);
        findings.Add(finding);
        evaluations.Add(new(rule, CloudFindingEvaluationStatus.Finding, severity, resource, evidence, null, title));
    }

    private static void AddUnknown(string rule, CloudResourceSnapshot resource, CloudAffectedResource affected, string explanation, string recommendation, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        var limitation = $"{rule}:{resource.ResourceType}:{resource.ResourceId}:evidence-insufficient";
        limitations.Add(limitation);
        var evidence = new[] { new CloudFindingEvidence($"snapshot:{resource.ResourceId}", "evidence=unknown") };
        findings.Add(new(rule, affected, CloudFindingSeverity.Medium, "Evidence is incomplete", explanation, evidence, limitation, new(recommendation), CloudFindingConfidence.Low, CloudFindingEvaluationStatus.Unknown, $"{rule}|{resource.ResourceType}|{resource.ResourceId}|unknown", false));
        evaluations.Add(new(rule, CloudFindingEvaluationStatus.Unknown, CloudFindingSeverity.Medium, affected, evidence, limitation, explanation));
    }

    private static void AddGlobalUnknown(string rule, string explanation, IReadOnlyList<string> evidenceValues, List<CloudRuleEvaluation> evaluations, List<CloudFinding> findings, List<string> limitations)
    {
        var limitation = $"{rule}:evidence-insufficient";
        limitations.Add(limitation);
        var evidence = evidenceValues.Select(value => new CloudFindingEvidence("snapshot", value)).ToArray();
        findings.Add(new(rule, new("snapshot", "snapshot", "unknown"), CloudFindingSeverity.Medium, "Snapshot evidence is incomplete", explanation, evidence, limitation, new("Collect a complete read-only snapshot before approval."), CloudFindingConfidence.Low, CloudFindingEvaluationStatus.Unknown, $"{rule}|snapshot|unknown", false));
        evaluations.Add(new(rule, CloudFindingEvaluationStatus.Unknown, CloudFindingSeverity.Medium, null, evidence, limitation, explanation));
    }

    private static void AddPass(string rule, CloudAffectedResource resource, List<CloudRuleEvaluation> evaluations)
        => evaluations.Add(new(rule, CloudFindingEvaluationStatus.Pass, CloudFindingSeverity.Low, resource, [], null, "Rule passed with available evidence."));

    private static void AddNotApplicable(string rule, CloudAffectedResource resource, List<CloudRuleEvaluation> evaluations)
        => evaluations.Add(new(rule, CloudFindingEvaluationStatus.NotApplicable, CloudFindingSeverity.Low, resource, [], null, "Rule is not applicable to this resource."));

    private static string? Property(CloudResourceSnapshot resource, string key)
        => resource.Properties?.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool? PropertyBool(CloudResourceSnapshot resource, string key)
        => bool.TryParse(Property(resource, key), out var value) ? value : null;

    private static int? PropertyInt(CloudResourceSnapshot resource, string key)
        => int.TryParse(Property(resource, key), out var value) ? value : null;

    private static string? Owner(CloudResourceSnapshot resource) => resource.Owner ?? Property(resource, "owner");

    private static bool IsCritical(CloudResourceSnapshot resource) => PropertyBool(resource, "critical") == true;

    private static bool IsProduction(CloudInfrastructureSnapshot snapshot, CloudResourceSnapshot resource)
        => string.Equals(Property(resource, "environment") ?? resource.Environment ?? snapshot.Environment, "production", StringComparison.OrdinalIgnoreCase);

    private static CloudFindingEvidence Evidence(CloudResourceSnapshot resource, string detail)
        => new($"snapshot:{resource.ResourceType}:{resource.ResourceId}", detail);

    private static int SeverityRank(CloudFindingSeverity severity) => severity switch
    {
        CloudFindingSeverity.Critical => 4,
        CloudFindingSeverity.High => 3,
        CloudFindingSeverity.Medium => 2,
        _ => 1
    };
}
