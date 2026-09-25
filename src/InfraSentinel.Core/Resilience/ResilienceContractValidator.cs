namespace InfraSentinel.Core.Resilience;

public interface IResilienceContractValidator
{
    ResilienceEvaluation Evaluate(ResilienceContractFixture fixture);
}

/// <summary>
/// Pure local resilience invariants. It evaluates declarations only and never
/// contacts a dependency or authorizes an external change.
/// </summary>
public sealed class ResilienceContractValidator : IResilienceContractValidator
{
    private static readonly string[] RuleIds =
    [
        "timeout-required",
        "retry-limited",
        "idempotency-required",
        "recovery-required",
        "failure-destination-required",
        "observability-minimum",
        "error-classification-explicit",
        "critical-dependency-behavior",
        "contract-approved",
        "human-approval-boundary"
    ];

    private static readonly ResilienceErrorKind[] RequiredErrorKinds =
        Enum.GetValues<ResilienceErrorKind>();

    public ResilienceEvaluation Evaluate(ResilienceContractFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (string.IsNullOrWhiteSpace(fixture.FixtureId))
            throw new ArgumentException("Fixture id is required.", nameof(fixture));
        if (string.IsNullOrWhiteSpace(fixture.Version))
            throw new ArgumentException("Fixture version is required.", nameof(fixture));
        if (string.IsNullOrWhiteSpace(fixture.ExecutionId))
            throw new ArgumentException("Execution id is required.", nameof(fixture));
        if (fixture.Components is null || fixture.Components.Count == 0)
            throw new ArgumentException("At least one resilience component is required.", nameof(fixture));

        var findings = new List<ResilienceFinding>();
        var passed = new List<string>();
        var evidence = fixture.Components.SelectMany(component => component.Evidence ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        EvaluateTimeouts(fixture, findings);
        EvaluateRetries(fixture, findings);
        EvaluateIdempotency(fixture, findings);
        EvaluateRecovery(fixture, findings);
        EvaluateFailureDestinations(fixture, findings);
        EvaluateObservability(fixture, findings);
        EvaluateErrorClassification(fixture, findings);
        EvaluateCriticalDependencies(fixture, findings);
        EvaluateContractStatus(fixture, findings);

        foreach (var rule in RuleIds)
        {
            if (!findings.Any(finding => finding.RuleId == rule))
                passed.Add(rule);
        }

        if (fixture.Components.Any(component => component.RequiresHumanApproval))
        {
            foreach (var component in fixture.Components.Where(component => component.RequiresHumanApproval))
            {
                findings.Add(new(
                    "human-approval-boundary",
                    component.ComponentId,
                    ResilienceFindingSeverity.Critical,
                    "This resilience contract is explicitly marked as requiring human approval.",
                    "Obtain explicit human approval before treating the contract as approved.",
                    true));
            }
            passed.Remove("human-approval-boundary");
        }
        else if (!findings.Any(finding => finding.RuleId == "human-approval-boundary"))
        {
            passed.Add("human-approval-boundary");
        }

        var requiresHuman = findings.Count > 0 || fixture.Components.Any(component => component.RequiresHumanApproval);
        var approved = !requiresHuman;
        ResilienceFindingSeverity? highest = findings.Count == 0 ? null : findings.Max(finding => finding.Severity);
        var justification = approved
            ? "All deterministic resilience contract rules passed."
            : $"Resilience contract requires human review because {findings.Count} finding(s) remain.";

        return new(
            fixture.ExecutionId,
            fixture.FixtureId,
            fixture.Version,
            fixture.Components.Select(component => component.ComponentId).ToArray(),
            RuleIds,
            passed.Distinct(StringComparer.Ordinal).ToArray(),
            findings,
            evidence,
            highest,
            approved,
            requiresHuman,
            justification);
    }

    private static void EvaluateTimeouts(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components)
        foreach (var dependency in component.Dependencies ?? [])
        {
            if (dependency.TimeoutSeconds is > 0) continue;
            findings.Add(new(
                "timeout-required",
                component.ComponentId,
                ResilienceFindingSeverity.High,
                $"External dependency '{dependency.Name}' has no explicit positive timeout.",
                "Declare a finite timeout for every external dependency.",
                true));
        }
    }

    private static void EvaluateRetries(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components)
        {
            var retry = component.RetryPolicy;
            if (retry is null)
            {
                if ((component.ErrorClassifications ?? []).Any(error => error.Retryable))
                    findings.Add(new("retry-limited", component.ComponentId, ResilienceFindingSeverity.High,
                        "The component classifies errors as retryable but declares no retry policy.",
                        "Declare a finite retry policy for every retryable error class.", true));
                continue;
            }

            if (retry.MaxAttempts is null or <= 0)
            {
                findings.Add(new("retry-limited", component.ComponentId, ResilienceFindingSeverity.Critical,
                    "Retry policy has no finite positive maximum attempt count.",
                    "Declare a finite retry limit.", true));
            }
            if (retry.Backoff == ResilienceBackoffStrategy.None)
            {
                findings.Add(new("retry-limited", component.ComponentId, ResilienceFindingSeverity.High,
                    "Retry policy has no explicit backoff strategy.",
                    "Declare fixed, exponential or jittered backoff.", true));
            }
            if (retry.RetryableErrors is null || retry.RetryableErrors.Count == 0)
            {
                findings.Add(new("retry-limited", component.ComponentId, ResilienceFindingSeverity.High,
                    "Retry policy does not classify retryable errors.",
                    "List the error classes allowed to retry.", true));
            }
            foreach (var error in retry.RetryableErrors ?? [])
            {
                if (error is ResilienceErrorKind.Authentication or ResilienceErrorKind.Validation or ResilienceErrorKind.Definitive)
                    findings.Add(new("error-classification-explicit", component.ComponentId, ResilienceFindingSeverity.High,
                        $"Retry policy indiscriminately retries {error} errors.",
                        "Do not retry authentication, validation or definitive errors without an explicit recovery decision.", true));

                if (!(component.ErrorClassifications ?? []).Any(classification => classification.Kind == error && classification.Retryable))
                    findings.Add(new("error-classification-explicit", component.ComponentId, ResilienceFindingSeverity.High,
                        $"Retry policy lists {error} as retryable without a matching retryable error classification.",
                        "Keep retry policy and error classification aligned.", true));
            }
        }
    }

    private static void EvaluateIdempotency(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components.Where(component => component.CanBeRepeated))
        {
            if (component.Idempotency is not null) continue;
            findings.Add(new("idempotency-required", component.ComponentId, ResilienceFindingSeverity.High,
                "A repeatable operation has no idempotency strategy.",
                "Declare an idempotency key, unique constraint, deduplication, natural idempotency or transaction control.", true));
        }
    }

    private static void EvaluateRecovery(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components.Where(component => component.OperationType is ResilienceOperationType.Asynchronous or ResilienceOperationType.Distributed))
        {
            if (!string.IsNullOrWhiteSpace(component.RecoveryStrategy)) continue;
            findings.Add(new("recovery-required", component.ComponentId, ResilienceFindingSeverity.High,
                "An asynchronous or distributed operation has no recovery strategy.",
                "Declare reconciliation, compensation, pending state, controlled replay or another recovery mechanism.", true));
        }
    }

    private static void EvaluateFailureDestinations(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components.Where(component => component.CanBeRepeated || component.OperationType is ResilienceOperationType.Asynchronous or ResilienceOperationType.Distributed))
        {
            if (!string.IsNullOrWhiteSpace(component.FailureDestination)) continue;
            var severity = component.Severity is ResilienceFindingSeverity.High or ResilienceFindingSeverity.Critical
                ? ResilienceFindingSeverity.High
                : ResilienceFindingSeverity.Medium;
            findings.Add(new("failure-destination-required", component.ComponentId, severity,
                "The operation has no declared dead-letter, quarantine or equivalent failure destination.",
                "Declare where permanently failed messages or work items are routed.", (int)severity >= (int)ResilienceFindingSeverity.High));
        }
    }

    private static void EvaluateObservability(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components)
        {
            if (component.ObservabilitySignals is { Count: > 0 }) continue;
            findings.Add(new("observability-minimum", component.ComponentId, ResilienceFindingSeverity.Medium,
                "The component declares no logs, metrics, traces, correlation id or alarms.",
                "Declare at least one observable signal and preferably a correlation mechanism.", true));
        }
    }

    private static void EvaluateErrorClassification(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components)
        {
            var classifications = component.ErrorClassifications ?? [];
            var duplicateKinds = classifications.GroupBy(error => error.Kind).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (duplicateKinds.Length > 0)
                findings.Add(new("error-classification-explicit", component.ComponentId, ResilienceFindingSeverity.High,
                    $"Error classification contains conflicting duplicate kinds: {string.Join(", ", duplicateKinds)}.",
                    "Declare exactly one handling decision per error class.", true));

            var classified = classifications.Select(error => error.Kind).ToHashSet();
            var missing = RequiredErrorKinds.Where(kind => !classified.Contains(kind)).ToArray();
            if (missing.Length > 0)
                findings.Add(new("error-classification-explicit", component.ComponentId, ResilienceFindingSeverity.High,
                    $"Error classification is incomplete; missing: {string.Join(", ", missing)}.",
                    "Classify temporary, timeout, authentication, validation, definitive and unknown errors.", true));

            if (classifications.Any(error => string.IsNullOrWhiteSpace(error.Handling)))
                findings.Add(new("error-classification-explicit", component.ComponentId, ResilienceFindingSeverity.High,
                    "At least one error class has no declared handling.",
                    "Document how each classified error is handled.", true));
        }
    }

    private static void EvaluateCriticalDependencies(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components)
        foreach (var dependency in component.Dependencies ?? [])
        {
            if (!dependency.Critical || !string.IsNullOrWhiteSpace(dependency.OutageBehavior)) continue;
            findings.Add(new("critical-dependency-behavior", component.ComponentId, ResilienceFindingSeverity.High,
                $"Critical dependency '{dependency.Name}' has no defined behavior during unavailability.",
                "Declare fallback, pending state, circuit breaker, timeout behavior or an explicit unavailability decision.", true));
        }
    }

    private static void EvaluateContractStatus(ResilienceContractFixture fixture, List<ResilienceFinding> findings)
    {
        foreach (var component in fixture.Components.Where(component => component.Status != ResilienceContractStatus.Approved))
            findings.Add(new("contract-approved", component.ComponentId, ResilienceFindingSeverity.High,
                $"Resilience contract status is {component.Status}; only Approved contracts can pass.",
                "Review and explicitly approve the resilience contract.", true));
    }
}
