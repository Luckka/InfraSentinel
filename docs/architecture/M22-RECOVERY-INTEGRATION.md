# M22 — InfraSentinel recovery integration

InfraSentinel consumes the generic recovery contract from `IAEngine.Core`.
Recovery state is namespaced under `.ai-state-infrasentinel/recovery`; finding
and gate artifacts remain under `.ai-runs-infrasentinel` and remain owned by
their existing validators.

The consumer does not add an executor, state machine, AWS path, or Rewind
adapter. `EngineHost` remains the execution boundary. `ExecutionKey` binds
project, milestone, task and execution identity. Artifact and checkpoint
identities are recorded through the generic service before any future external
operation is authorized.

M22 does not claim terminal milestone continuation until the IAEngine workflow
integration can reopen a persisted terminal run through the existing
`MilestoneRunner` and `Orchestrator`. Rewind remains read-only contextual
retrieval and is never used to resume execution.
