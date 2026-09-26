# M13 — Observability and Evidence Gate

## Status

Implemented locally on `feature/m13-observability-evidence-gate`. This gate is
deterministic and local; it does not access AWS, providers, customer data, or
external infrastructure.

## Problem and boundary

The earlier gates explained whether an architecture, resilience contract, or
security invariant passed. M13 adds the audit trail needed to explain how that
conclusion was reached. The evidence model belongs to InfraSentinel because it
records Sentinel-specific fixtures, findings, remediation, review, approval, and
checkpoint decisions. IAEngine remains responsible for orchestration, state,
approval workflow, persistence, and the generic checkpoint contract.

The implementation uses adapters around the existing `IValidationRunner`,
`IReviewAgent`, and `IGitCheckpointCoordinator`. No second executor or state
machine was introduced.

## Artifact

Every completed checkpoint writes:

```text
.ai-runs-infrasentinel/observability-evidence-gate.json
```

The artifact is schema-versioned (`1.0`) and contains execution identity,
milestone and task IDs, fixture metadata, consumed Engine revision, ordered
events, validations, findings, severities, attempts, retries, remediation,
review, approval, checkpoint decision, branch, changed files, commit SHA, and
explicit `pushPerformed`/`mergePerformed` flags. The writer creates only the
Sentinel run directory; it never creates `.ai-runs` or `.ai-state`.

Timestamps and generated execution IDs are intentionally runtime metadata. The
logical evidence is stable: event sequence, rule order, task order, findings,
severities, decisions, and boundary flags are sorted or recorded explicitly.

## Recorded events

The aggregator records `Task`, `Validation`, `Retry`, `Remediation`, `Review`,
`Approval`, and `Checkpoint` events. A validation after the first attempt adds a
bounded retry and remediation event. Findings are attached to the validation
that produced them, so a remediation does not silently erase the original
evidence.

The evidence validator checks identity, task coverage, validation, review,
approval, checkpoint, and the no-push/no-merge boundary. Missing evidence is a
finding, not an implicit success.

## Required outcomes

- A valid local fixture reaches `CompleteAwaitingApproval`, then `Approved`
  only after `ApproveMilestoneAsync` and a successful local checkpoint.
- Medium/High findings remain visible in the artifact and cannot disappear
  through an unrecorded remediation.
- Critical findings remain `HumanRequired`; no commit, push, or merge is
  allowed before explicit human handling.
- Validation failures and unrelated-diff decisions are preserved as blocked
  evidence and do not create commits.

## Relationship with previous milestones

- M8 established architecture-defense findings and explicit approval.
- M9 established deterministic resilience findings and failure controls.
- M11-C connected the EngineHost workflow to a Sentinel-owned checkpoint
  coordinator without push or merge.
- M12 established security invariant validation and the critical finding
  boundary.
- M13 makes those execution decisions auditable and reproducible as a single
  evidence artifact.

## SOLID and Clean Architecture review

The aggregator collects evidence, the adapters translate existing Engine
contracts, the writer persists the artifact, the evidence validator checks
completeness, and the EngineHost remains the workflow owner. No security,
resilience, or architecture rule was added to IAEngine.Core. No provider,
service locator, global state, or synchronous blocking was introduced.

## Limitations and risks

The current artifact is local and process-scoped. It does not provide tamper
proofing, remote retention, distributed correlation, or external telemetry.
The generic Engine state remains the source of truth for workflow status; the
M13 model observes it rather than replacing it. A future milestone may add
schema evolution and durable evidence retention after a human decision.

## Example blocked evidence

```json
{
  "status": "HumanRequired",
  "findings": [{
    "ruleId": "SECURITY-CRITICAL",
    "severity": "Critical",
    "explanation": "Explicit human approval is required."
  }],
  "pushPerformed": false,
  "mergePerformed": false
}
```

## Learning Checkpoint

1. Evidence belongs to InfraSentinel because its meaning is defined by Sentinel
   fixtures and gates; the IAEngine only executes generic workflow contracts.
2. The IAEngine provides orchestration, state transitions, retries, approval,
   persistence, and checkpoint policy. InfraSentinel supplies domain findings
   and evidence semantics.
3. Deterministic findings are insufficient without recorded inputs, attempts,
   review, approval, and checkpoint context; the artifact provides that audit
   trail.
4. A Critical finding requires human authority because accepting it changes the
   security posture and is outside safe automatic remediation.
5. `CompleteAwaitingApproval` means workflow gates completed but approval has
   not been granted; `Approved` means approval and checkpoint gates passed;
   `HumanRequired` is an explicit stop requiring human action.
6. Synthetic fixtures make rule behavior reproducible before any future
   decision to connect a real provider or infrastructure.
