# ADR-0007 — Local resilience contract gate

Status: Proposed; not automatically approved.

## Context

After the M8 Architecture Defense Gate, InfraSentinel needs a deterministic
contract for checking whether a synthetic architecture declares minimum
resilience controls before approval. The check must remain local and must not
pretend to validate live infrastructure.

## Decision proposed

Keep resilience models, rules, severities, findings and recommendations in
`InfraSentinel.Core.Resilience`. Adapt the result to IAEngine's existing
`IValidationRunner` contract. Reuse `EngineHost`, the Orchestrator validation
stage, `RunStore`, milestone state and explicit approval without adding a new
IAEngine abstraction.

The gate treats missing timeout, unbounded retry, missing idempotency,
recovery, failure routing, observability, error classification or critical
dependency behavior as deterministic findings. Any finding prevents automatic
approval and is represented as `HUMAN_REQUIRED` by the existing workflow.

## Safety boundary

The implementation evaluates only local fixtures. It does not access AWS,
Kubernetes, databases, APIs, customer code, external providers, secrets or
network endpoints. Artifacts remain under the InfraSentinel-specific run and
state directories.

## Consequences

- InfraSentinel can express and test resilience invariants without moving domain
  policy into IAEngine.Core.
- The same EngineHost can execute this or another project's invariant set.
- Declared controls are checked, but live runtime behavior is not proven.
- Evidence retention, redaction, schema versioning and human approval UX remain
  future decisions.
