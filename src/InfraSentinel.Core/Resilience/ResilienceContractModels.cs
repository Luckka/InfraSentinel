namespace InfraSentinel.Core.Resilience;

public enum ResilienceOperationType
{
    Synchronous,
    Asynchronous,
    Distributed
}

public enum ResilienceFindingSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum ResilienceContractStatus
{
    Draft,
    Proposed,
    Approved,
    Rejected
}

public enum ResilienceErrorKind
{
    Temporary,
    Timeout,
    Authentication,
    Validation,
    Definitive,
    Unknown
}

public enum ResilienceBackoffStrategy
{
    None,
    Fixed,
    Exponential,
    ExponentialWithJitter
}

public enum ResilienceIdempotencyStrategy
{
    IdempotencyKey,
    UniqueConstraint,
    Deduplication,
    NaturallyIdempotent,
    Transactional
}

public sealed record ResilienceDependency(
    string DependencyId,
    string Name,
    bool Critical,
    int? TimeoutSeconds,
    string? OutageBehavior);

public sealed record ResilienceRetryPolicy(
    int? MaxAttempts,
    ResilienceBackoffStrategy Backoff,
    IReadOnlyList<ResilienceErrorKind> RetryableErrors);

public sealed record ResilienceErrorClassification(
    ResilienceErrorKind Kind,
    bool Retryable,
    string Handling);

public sealed record ResilienceComponent(
    string ComponentId,
    string Service,
    ResilienceOperationType OperationType,
    IReadOnlyList<ResilienceDependency> Dependencies,
    ResilienceRetryPolicy? RetryPolicy,
    bool CanBeRepeated,
    ResilienceIdempotencyStrategy? Idempotency,
    string? RecoveryStrategy,
    string? FailureDestination,
    IReadOnlyList<string> ObservabilitySignals,
    IReadOnlyList<ResilienceErrorClassification> ErrorClassifications,
    ResilienceContractStatus Status,
    IReadOnlyList<string> Evidence,
    ResilienceFindingSeverity Severity,
    bool RequiresHumanApproval);

public sealed record ResilienceContractFixture(
    string FixtureId,
    string Version,
    IReadOnlyList<ResilienceComponent> Components,
    string ExecutionId);

public sealed record ResilienceFinding(
    string RuleId,
    string ComponentId,
    ResilienceFindingSeverity Severity,
    string Explanation,
    string Recommendation,
    bool RequiresHumanApproval);

public sealed record ResilienceEvaluation(
    string ExecutionId,
    string FixtureId,
    string FixtureVersion,
    IReadOnlyList<string> ComponentsEvaluated,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<ResilienceFinding> Findings,
    IReadOnlyList<string> Evidence,
    ResilienceFindingSeverity? HighestSeverity,
    bool Approved,
    bool RequiresHumanApproval,
    string Justification)
{
    public string Status => Approved ? "Approved" : "HumanRequired";
}

public sealed record ResilienceContractArtifact(
    string ExecutionId,
    string FixtureId,
    string FixtureVersion,
    IReadOnlyList<string> ComponentsEvaluated,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<ResilienceFinding> Findings,
    IReadOnlyList<ResilienceFindingSeverity> Severities,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Recommendations,
    string Status,
    bool RequiresHumanApproval,
    string Justification);
