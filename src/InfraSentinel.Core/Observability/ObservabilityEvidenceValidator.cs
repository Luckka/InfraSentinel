namespace InfraSentinel.Core.Observability;

public sealed class ObservabilityEvidenceValidator
{
    private static readonly string[] Rules =
    [
        "execution-identity-present",
        "task-evidence-present",
        "validation-evidence-present",
        "review-evidence-present",
        "approval-evidence-present",
        "checkpoint-evidence-present",
        "push-merge-boundary"
    ];

    public ObservabilityEvidenceValidation Validate(ObservabilityEvidenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var findings = new List<ObservabilityEvidenceFinding>();
        var passed = new List<string>();
        Check(string.IsNullOrWhiteSpace(snapshot.ExecutionId) || string.IsNullOrWhiteSpace(snapshot.ProjectId), "execution-identity-present", "Execution and project identity are required.", findings);
        Check(snapshot.TaskIds.Count == 0 || !snapshot.TaskIds.All(task => snapshot.Events.Any(item => item.TaskId == task)), "task-evidence-present", "Every declared task must have evidence.", findings);
        Check(!snapshot.Events.Any(item => item.Kind == ObservabilityEvidenceEventKind.Validation), "validation-evidence-present", "At least one validation event is required.", findings);
        Check(snapshot.ReviewStatus == "NotStarted", "review-evidence-present", "Review evidence is missing.", findings);
        Check(snapshot.ApprovalStatus == "Pending", "approval-evidence-present", "Approval evidence is missing.", findings);
        Check(snapshot.CheckpointDecision is null, "checkpoint-evidence-present", "Checkpoint decision evidence is missing.", findings);
        Check(snapshot.PushPerformed || snapshot.MergePerformed, "push-merge-boundary", "Push and merge must remain false.", findings);
        foreach (var rule in Rules.Where(rule => !findings.Any(finding => finding.RuleId == rule))) passed.Add(rule);
        return new(findings.Count == 0, Rules, passed, findings, findings.Count == 0 ? "All evidence rules passed." : "Evidence is incomplete or violates a boundary.");
    }

    private static void Check(bool failed, string ruleId, string explanation, List<ObservabilityEvidenceFinding> findings)
    {
        if (!failed) return;
        findings.Add(new(ruleId, null, "High", explanation, [ruleId]));
    }
}
