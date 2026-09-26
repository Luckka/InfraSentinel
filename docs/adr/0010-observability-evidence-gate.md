# ADR 0010 — Observability and Evidence Gate

## Status

Accepted for the M13 local implementation.

## Context

InfraSentinel already had domain gates for architecture, resilience, and
security. Their results were useful but did not by themselves prove which
tasks, attempts, findings, reviews, approvals, and Git decisions produced a
result. The EngineHost and checkpoint coordinator already provide the generic
workflow boundary needed to observe this information.

There is an existing historical ADR file using the `0010` prefix for the
security gate. This M13 filename is intentionally kept as requested by the
milestone; the next ADR numbering decision should reconcile that legacy
collision.

## Decision

Add an InfraSentinel-owned, process-scoped evidence aggregator and deterministic
artifact writer. Integrate it through adapters around existing Engine contracts:

```text
EngineHost → validation/review → approval → checkpoint coordinator
          ↘ Sentinel evidence adapters → observability-evidence-gate.json
```

The artifact records logical evidence in stable order and explicitly records
that push and merge were not performed. It does not create commits itself.

## Consequences

Positive:

- executions become explainable and auditable;
- retries and remediation cannot erase original findings;
- approval and checkpoint boundaries are visible;
- generic IAEngine contracts remain unchanged;
- tests can compare equivalent logical executions.

Negative or deferred:

- timestamps and execution IDs remain runtime metadata;
- local artifacts are not tamper-proof or remotely retained;
- a later schema/versioning policy is needed before external consumers depend
  on the artifact format;
- the ADR numbering collision requires human cleanup in a later documentation
  pass.

## Alternatives rejected

- Adding evidence models to IAEngine.Core would invert the dependency direction.
- Creating a second orchestrator would duplicate state-machine behavior.
- Writing directly from validators would mix domain validation with persistence
  and Git workflow.
- Calling external telemetry or AWS would violate the local deterministic scope.

## Verification

The M13 tests cover logical determinism, retry/remediation evidence, incomplete
evidence, EngineHost execution, explicit approval, HumanRequired, checkpoint
artifacts, and unrelated-diff blocking. The full InfraSentinel and IAEngine
Core suites are run sequentially before completion.
