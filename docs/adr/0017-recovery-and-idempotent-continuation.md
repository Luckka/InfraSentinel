# ADR 0017: Recovery and idempotent continuation remain generic Engine concerns

## Status

Blocked by `ENGINE_CONTRACT_GAP` in the current IAEngine contract.

## Context

M20 requires a reproducible interruption and restart workflow. The workflow
must continue a milestone from a real persisted state, skip completed tasks,
preserve findings and artifacts, distinguish retry from recovery, and require
explicit human approval before a HumanRequired execution resumes.

The existing `EngineHost` delegates milestone execution to the existing
`MilestoneRunner` and state store. This is the correct architectural boundary,
but the public continuation contract discovers only active/non-terminal runs.
Terminal failed runs lose the active pointer, while HumanRequired runs can be
reopened through the existing orchestrator recovery path without a generic
milestone approval token.

## Decision

InfraSentinel will not create a parallel recovery state machine, custom
continuation executor, or project-specific workaround. IAEngine must first
expose a reviewed generic recovery contract. M20 validation will then use the
existing EngineHost, MilestoneRunner, Orchestrator, RoadmapStateStore, and
checkpoint coordinator through that contract.

The eventual idempotency key is:

```text
projectId / milestoneId / taskId / executionId / attemptNumber
```

Timestamps may describe ordering but are not identity. Artifacts and
checkpoints must be compare-and-preserve operations for that key.

## Consequences

- No IAEngine or OnlineOS files are changed in M20.
- No AWS calls are needed or permitted.
- M20 cannot honestly claim recovery-after-failure, explicit HumanRequired
  approval continuation, or crash recovery until the generic contract exists.
- The contract gap is explicit and actionable for the next reviewed milestone.
