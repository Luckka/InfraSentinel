namespace InfraSentinel.Core.Architecture;

public enum ArchitectureDecisionStatus
{
    Draft,
    Proposed,
    Approved,
    Rejected
}

public enum ArchitectureAdrStatus
{
    Missing,
    Draft,
    Proposed,
    Approved,
    Rejected
}

public enum ArchitectureFindingSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum ArchitectureDefenseStatus
{
    Approved,
    HumanRequired
}

public sealed record ArchitectureDecision(
    string DecisionId,
    string Title,
    string Context,
    string Problem,
    string Decision,
    IReadOnlyList<string> AlternativesConsidered,
    IReadOnlyList<string> Consequences,
    IReadOnlyList<string> Evidence,
    string? AdrReference,
    ArchitectureAdrStatus AdrStatus,
    ArchitectureDecisionStatus Status,
    bool RequiresHumanApproval,
    ArchitectureFindingSeverity Severity);

public sealed record ArchitectureDecisionFixture(IReadOnlyList<ArchitectureDecision> Decisions)
{
    public static ArchitectureDecisionFixture Single(ArchitectureDecision decision) => new([decision]);
}

public sealed record ArchitectureDefenseFinding(
    string RuleId,
    string DecisionId,
    ArchitectureFindingSeverity Severity,
    string Explanation,
    bool RequiresHumanApproval);

public sealed record ArchitectureDefenseEvaluation(
    string DecisionId,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<ArchitectureDefenseFinding> Findings,
    ArchitectureDefenseStatus Status,
    bool RequiresHumanApproval,
    string Justification)
{
    public bool Approved => Status == ArchitectureDefenseStatus.Approved;
}

public sealed record ArchitectureDefenseArtifact(
    string DecisionId,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<ArchitectureDefenseFinding> Findings,
    IReadOnlyList<ArchitectureFindingSeverity> Severities,
    string Justification,
    ArchitectureDefenseStatus Status,
    bool RequiresHumanApproval);
