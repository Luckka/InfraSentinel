# M11-C — InfraSentinel Engine-Controlled Checkpoint

## Decision

The historical `OnlineOs.AiOrchestrator.*` namespace remains temporarily for
compatibility. InfraSentinel consumes the generic checkpoint contract from
`IAEngine.Core` revision `a7b387e`; it does not reference
`IAEngine.OnlineOSAdapter`.

## Flow

```text
EngineHost.RunMilestoneAsync
  → ordered Sentinel tasks
  → deterministic validation
  → review
  → CompleteAwaitingApproval
  → EngineHost.ApproveMilestoneAsync
  → IGitCheckpointCoordinator
  → generic policy + live Git state
  → local semantic commit or blocked result
```

The coordinator is an InfraSentinel integration component. `EngineHost` remains
responsible for orchestration, state, approval and checkpoint invocation. The
Core policy decides generic commit eligibility; Sentinel supplies the concrete
Git state reader and local commit implementation.

## Milestone

`sentinel-engine-controlled-checkpoint` contains six dependent tasks:

1. `CHECKPOINT-001` — load Sentinel workspace;
2. `CHECKPOINT-002` — execute deterministic validation;
3. `CHECKPOINT-003` — execute review decision;
4. `CHECKPOINT-004` — evaluate generic checkpoint policy;
5. `CHECKPOINT-005` — create semantic commit when allowed;
6. `CHECKPOINT-006` — persist checkpoint result.

The task graph is provided through `IEngineMilestoneSource` and executed by the
existing `MilestoneRunner`; InfraSentinel does not introduce another executor or
state machine.

## Coordinator boundary

`InfraSentinelGitCheckpointCoordinator`:

- reads branch, status, root, diff and commit SHA through `IGitService`;
- checks diff whitespace, conflicts, secrets, expected files and feature-branch
  authorization;
- delegates the generic decision to `GitCheckpointPolicy`;
- uses the generic `IProcessRunner` only for local `git add` and `git commit`;
- returns the commit SHA;
- never runs push, merge, branch creation or branch switching.

The validator and security/resilience rules remain outside the coordinator.

## Approval and blocked states

`CompleteAwaitingApproval`, `HumanRequired`, failed validation, failed review,
invalid semantic messages, unrelated files, conflicts, secrets and unauthorized
branches cannot create a commit. The only approval path is the explicit
`EngineHost.ApproveMilestoneAsync` call followed by a fresh live-state evaluation.

## Artifact

Checkpoint artifacts are written below:

```text
.ai-runs-infrasentinel/checkpoint/<task-id>.json
```

They include validation/review status, decision, reason, branch, files, commit
SHA, human approval, timestamp, and explicit `pushPerformed: false` and
`mergePerformed: false`. Runtime directories are ignored by the Sentinel
repository and are never generic `.ai-runs` or `.ai-state` directories.

## Verification

The integration test uses a temporary local Git repository and proves that:

- no commit exists before explicit approval;
- `ApproveMilestoneAsync` drives the checkpoint;
- a semantic commit and SHA are returned;
- HumanRequired and unrelated/unauthorized changes are blocked;
- push and merge remain false;
- artifacts are isolated;
- InfraSentinel references Core but not the OnlineOS adapter.

No AWS, provider, external infrastructure, OnlineOS checkout, push or merge was
used.

## Limitations and next steps

The current generic contract exposes read-only `IGitService` operations, so the
Sentinel coordinator uses the generic process runner for the two permitted local
write commands. A future generic API may introduce a dedicated local commit
operation if multiple consumers need the same mechanism. That is intentionally
not added in M11-C.
