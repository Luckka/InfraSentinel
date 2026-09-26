namespace InfraSentinel.Core.Security;

public enum SecurityResourceClassification
{
    Public,
    Internal,
    Sensitive,
    Critical
}

public enum SecurityExposure
{
    Public,
    Internal,
    Private
}

public enum SecurityFindingSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum SecurityFindingStatus
{
    Passed,
    Failed
}

public enum SecurityInvariantStatus
{
    Approved,
    HumanRequired
}

public sealed record SecurityResource(
    string ResourceId,
    SecurityResourceClassification Classification,
    SecurityExposure Exposure,
    bool EncryptionEnabled,
    bool AuditLoggingEnabled,
    bool Critical,
    string? Owner);

public sealed record SecurityPermission(
    string PermissionId,
    string Action,
    string ResourceScope,
    bool Excessive,
    string Evidence);

public sealed record SecurityIdentity(
    string IdentityId,
    string DeclaredPurpose,
    IReadOnlyList<SecurityPermission> Permissions);

public sealed record SecuritySecret(
    string SecretId,
    string Value,
    bool Plaintext);

public sealed record SecurityDependency(
    string DependencyId,
    string Name,
    bool Critical,
    string? Owner,
    string? OutageBehavior);

public sealed record SecurityInvariantFixture(
    string FixtureId,
    string Version,
    string ExecutionId,
    IReadOnlyList<SecurityResource> Resources,
    IReadOnlyList<SecurityIdentity> Identities,
    IReadOnlyList<SecuritySecret> Secrets,
    IReadOnlyList<SecurityDependency> Dependencies,
    IReadOnlyList<string> Evidence);

public sealed record SecurityInvariantFinding(
    string RuleId,
    string? ResourceId,
    SecurityFindingSeverity Severity,
    string Title,
    string Explanation,
    IReadOnlyList<string> Evidence,
    string RemediationSuggestion,
    SecurityFindingStatus Status,
    bool RequiresHumanApproval);

public sealed record SecurityInvariantEvaluation(
    string ExecutionId,
    string FixtureId,
    string FixtureVersion,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<SecurityInvariantFinding> Findings,
    SecurityInvariantStatus Status,
    bool RequiresHumanApproval,
    string Justification)
{
    public bool Approved => Status == SecurityInvariantStatus.Approved;
    public SecurityApprovalDecision Decision
        => new(Status, RequiresHumanApproval, Justification);
}

public sealed record SecurityApprovalDecision(
    SecurityInvariantStatus Status,
    bool RequiresHumanApproval,
    string Reason);

public sealed record SecurityInvariantArtifact(
    string ExecutionId,
    string MilestoneId,
    string TaskId,
    DateTimeOffset Timestamp,
    string FixtureId,
    string FixtureVersion,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<SecurityInvariantFinding> Findings,
    IReadOnlyList<SecurityFindingSeverity> Severities,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Remediation,
    string Status,
    string ApprovalStatus,
    bool RequiresHumanApproval,
    string CommitStatus);
