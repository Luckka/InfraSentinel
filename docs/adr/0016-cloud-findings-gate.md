# ADR 0016: Cloud Findings Gate

## Status

Accepted for M19.

## Decision

Use a pure InfraSentinel domain gate over the neutral M18 cloud snapshot. Keep rule evaluation, severity, deterministic ordering, evidence, limitations, and remediation recommendations inside InfraSentinel. Delegate execution lifecycle, review, approval, and semantic checkpointing to the existing EngineHost contracts.

The gate is fail-closed: blocked collection blocks the gate, unknown evidence remains unknown, and every remediation recommendation is non-automatic. The AWS SDK and live provider remain outside the domain.

## Context

M18 provides sanitized, read-only cloud observations. M19 needs to turn those observations into explainable security, resilience, observability, cost, and governance findings without introducing an executor or a second workflow state machine.

## Consequences

Positive consequences:

- deterministic findings can be tested entirely offline;
- AWS and future cloud providers remain replaceable;
- incomplete evidence is visible rather than silently treated as safe;
- artifacts provide a reviewable local audit trail;
- no remediation can accidentally mutate AWS.

Trade-offs:

- a finding is a recommendation, not enforcement;
- unknown evidence can require another observation run;
- actual AWS posture depends on the completeness and freshness of the M18 snapshot;
- human review remains mandatory for critical/high findings and explicit approval remains separate from evaluation.

## Rejected alternatives

- Automatic remediation was rejected because it would expand M19 into a mutating control plane.
- A second state machine was rejected because EngineHost already owns milestone, review, approval, and checkpoint transitions.
- AWS SDK types in the domain were rejected because they would prevent fake/offline and non-AWS providers.
