namespace InfraSentinel.Core.Architecture;

public interface IArchitectureDefenseValidator
{
    ArchitectureDefenseEvaluation Evaluate(ArchitectureDecisionFixture fixture);
}

/// <summary>
/// Pure, local and deterministic architecture decision defense rules.
/// It does not inspect repositories, call providers or approve human-sensitive
/// decisions.
/// </summary>
public sealed class ArchitectureDefenseValidator : IArchitectureDefenseValidator
{
    private static readonly string[] RuleIds =
    [
        "adr-required",
        "adr-approved",
        "decision-approved",
        "alternatives-required",
        "consequences-required",
        "evidence-required-for-sensitive-decision",
        "human-approval-boundary"
    ];

    public ArchitectureDefenseEvaluation Evaluate(ArchitectureDecisionFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (fixture.Decisions is null || fixture.Decisions.Count != 1)
            throw new ArgumentException("The architecture defense fixture must contain exactly one decision.", nameof(fixture));

        var decision = fixture.Decisions[0];
        var findings = new List<ArchitectureDefenseFinding>();
        var passed = new List<string>();

        if (string.IsNullOrWhiteSpace(decision.AdrReference) || decision.AdrStatus == ArchitectureAdrStatus.Missing)
            findings.Add(new("adr-required", decision.DecisionId, ArchitectureFindingSeverity.High,
                "A relevant architecture decision must reference a corresponding ADR.", true));
        else passed.Add("adr-required");

        if (decision.AdrStatus == ArchitectureAdrStatus.Approved)
            passed.Add("adr-approved");
        else
            findings.Add(new("adr-approved", decision.DecisionId, ArchitectureFindingSeverity.High,
                $"The referenced ADR has status {decision.AdrStatus}; only Approved ADRs can support architectural approval.", true));

        if (decision.Status == ArchitectureDecisionStatus.Approved)
            passed.Add("decision-approved");
        else
            findings.Add(new("decision-approved", decision.DecisionId, ArchitectureFindingSeverity.High,
                $"The architecture decision has status {decision.Status}; only Approved decisions can pass the defense gate.", true));

        if (HasContent(decision.AlternativesConsidered))
            passed.Add("alternatives-required");
        else
            findings.Add(new("alternatives-required", decision.DecisionId, ArchitectureFindingSeverity.High,
                "At least one considered alternative is required for a relevant architecture decision.", false));

        if (HasContent(decision.Consequences))
            passed.Add("consequences-required");
        else
            findings.Add(new("consequences-required", decision.DecisionId, ArchitectureFindingSeverity.High,
                "Documented consequences are required for a relevant architecture decision.", false));

        var sensitive = decision.Severity is ArchitectureFindingSeverity.High or ArchitectureFindingSeverity.Critical;
        if (!sensitive || HasContent(decision.Evidence))
            passed.Add("evidence-required-for-sensitive-decision");
        else
            findings.Add(new("evidence-required-for-sensitive-decision", decision.DecisionId, ArchitectureFindingSeverity.High,
                "A sensitive architecture decision requires local evidence or an associated test before approval.", true));

        var humanRequired = decision.RequiresHumanApproval || findings.Count > 0;
        if (decision.RequiresHumanApproval)
            findings.Add(new("human-approval-boundary", decision.DecisionId, ArchitectureFindingSeverity.Critical,
                "This decision is marked as requiring human approval; the gate cannot approve it automatically.", true));
        else
            passed.Add("human-approval-boundary");

        var status = findings.Count == 0 && !humanRequired
            ? ArchitectureDefenseStatus.Approved
            : ArchitectureDefenseStatus.HumanRequired;
        var justification = status == ArchitectureDefenseStatus.Approved
            ? "All deterministic architecture defense rules passed."
            : $"Architecture defense requires human review because {findings.Count} finding(s) remain.";

        return new ArchitectureDefenseEvaluation(
            decision.DecisionId,
            RuleIds,
            passed,
            findings,
            status,
            humanRequired,
            justification);
    }

    private static bool HasContent(IReadOnlyList<string>? values) =>
        values is not null && values.Any(value => !string.IsNullOrWhiteSpace(value));
}
