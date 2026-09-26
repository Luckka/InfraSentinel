# ADR-0009 — InfraSentinel Engine-Controlled Checkpoint

## Status

Accepted for M11-C.

## Context

IAEngine.Core provides `IGitCheckpointCoordinator` and a fail-closed generic
policy. InfraSentinel needed to exercise that contract through `EngineHost`
without reusing the historical OnlineOS Git workflow or creating a second
milestone executor.

## Decision

InfraSentinel owns `InfraSentinelGitCheckpointCoordinator`. It reads Git state
through `IGitService`, evaluates the Core policy, and performs only local
`git add`/`git commit` operations through `IProcessRunner` after the EngineHost
approval flow has completed. Push, merge, branch creation and branch switching
are outside the coordinator.

The existing historical namespace is accepted temporarily for compatibility.
The adapter remains unused by InfraSentinel.

## Consequences

Positive:

- EngineHost remains the sole workflow and milestone orchestrator.
- Sentinel-specific integration stays outside IAEngine.Core.
- HumanRequired and invalid Git state fail closed.
- The result is explainable and persisted in an isolated artifact.

Trade-offs:

- `IGitService` remains read-only, so the concrete consumer uses the generic
  process runner for local commit commands.
- Artifact timestamps are runtime values and are not deterministic across runs.
- A later generic commit-service contract may remove that consumer-side detail.

## Rejected alternatives

- Reusing `GitWorkflowManager`: rejected because it owns historical OnlineOS
  branches, staging paths, push and merge behavior.
- Adding Sentinel rules to IAEngine.Core: rejected because it reverses ownership
  and would couple the generic engine to one consumer.
- Creating a parallel milestone runner: rejected because it would duplicate state,
  recovery and approval behavior.
