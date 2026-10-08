# Recovery Runbook

## Current status

The generic IAEngine transition is available at revision `ddb226e`. Use the
public EngineHost recovery API; do not edit `roadmap-state.json`, replay private
runner methods, or create a Sentinel-specific executor. Do not run AWS.

## Intended workflow after the generic contract is available

1. Use a temporary workspace containing only
   `.ai-runs-infrasentinel/recovery-tests/` and
   `.ai-state-infrasentinel/recovery-tests/`.
2. Start the milestone with `EngineHost.RunMilestoneAsync` and a fake provider.
3. Confirm every task transition and artifact is persisted before injecting a
   deterministic timeout, cancellation, or process interruption.
4. Recreate the host from the same state store and call the public recovery API
   for terminal runs, or `ContinueMilestoneAsync` for active runs.
5. Select only pending or explicitly recoverable tasks. Never rerun a task in
   `Done` state.
6. Record retry attempts separately from recovery attempts. Apply a finite
   retry limit.
7. Stop at `HumanRequired`. Continue only after an explicit approval operation;
   approval must be recorded in the artifact chain.
8. Request a local checkpoint only after the milestone is approved. The
   checkpoint operation must be idempotent and must not push or merge.
9. Calling recovery or approval again must return the same logical state and
   must not add a task execution, finding, artifact, checkpoint, or commit.

## Safe handling of special states

- `Done`: immutable for recovery; skip execution.
- `Running`: recover from persisted execution metadata or mark abandoned using
  the generic administrative operation.
- `Failed`: retry only when the persisted failure is classified as recoverable.
- `HumanRequired`: wait for explicit approval; do not infer approval from a
  second continue call.
- `Pending`: execute only when dependencies are `Done`.
- `Approved`: permit the local checkpoint exactly once.
- `Abandoned`: requires a new explicit execution/attempt; do not silently
  resurrect the old one.

## Limits of this validation

The M24 integration validates local deterministic HumanRequired, timeout,
cancellation, and process-crash/restart recovery via `EngineHost`, original run
identity, preserved artifacts, idempotent recovery/approval, and no premature
checkpoint. Live-provider, AWS, and OnlineOS behavior remain out of scope.
