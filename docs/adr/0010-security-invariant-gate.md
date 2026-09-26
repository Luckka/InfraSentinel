# ADR-0010 — Security Invariant Gate

## Status

Accepted for M12.

## Context

InfraSentinel needs a local, explainable security capability without moving
security semantics into IAEngine.Core or accessing real infrastructure. Earlier
milestones established Architecture Defense, Resilience Contract validation and
the Engine-controlled checkpoint boundary.

## Decision

Implement a pure `SecurityInvariantValidator` and domain models in InfraSentinel.
Expose it through `IValidationRunner`, execute it using the existing EngineHost
milestone workflow, and reuse the M11-C checkpoint coordinator only after
validation, review, remediation and explicit approval succeed.

Critical findings always produce `HumanRequired`. Plaintext secrets are Critical.
The validator never creates commits or executes external commands.

## Consequences

- Security rules remain replaceable and consumer-owned.
- Findings and artifacts are explainable and deterministic for the same fixture.
- The Core workflow remains unchanged and reusable by other consumers.
- Synthetic fixtures cannot establish deployed-infrastructure security.
- A future generic artifact schema may be considered if multiple consumers need
  the same execution metadata.

## Rejected alternatives

- Add security rules to IAEngine.Core: rejected because it couples the generic
  engine to InfraSentinel semantics.
- Run AWS or a real scanner: rejected because M12 is local and deterministic.
- Commit from the validator: rejected because Git control belongs to the
  EngineHost/coordinator boundary.
- Add a parallel security state machine: rejected because milestone state belongs
  to IAEngine.
