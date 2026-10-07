# M20 — Recovery, Continuation and Idempotent Execution

## Result

M20 was started on `feature/m20-recovery-continuation` with AWS disabled. The
required contract audit found an IAEngine contract gap before a valid recovery
validation could be implemented without duplicating the Engine state machine.

`EngineHost` does expose `RunMilestoneAsync`, `ContinueMilestoneAsync`, and
`ApproveMilestoneAsync`. However, the current generic implementation cannot
represent the complete M20 lifecycle:

1. `MilestoneRunner` marks a task as `Blocked` and the milestone as `Failed`
   when the task run is terminally failed.
2. `RunStore` removes the active pointer for `Failed` runs, and
   `EngineHost.ContinueMilestoneAsync` only discovers an active/non-terminal
   run. A terminal task failure therefore cannot be continued through the
   public EngineHost API.
3. `HumanRequired` is discoverable, but `ContinueMilestoneAsync` delegates to
   the existing autonomous recovery path. There is no generic explicit
   approval transition for a HumanRequired task or milestone. Calling continue
   can therefore reopen the task without the approval boundary required by
   M20.
4. Milestone state records task completion, but do not persist a resumable
   recovery record containing the execution key, attempt, artifact chain, and
   recovery reason required by M20.

These are generic IAEngine lifecycle concerns. InfraSentinel does not add a
second executor, alter IAEngine, alter OnlineOS, or use AWS as a workaround.

## Required generic contract

The next reviewed IAEngine change must provide, at minimum:

- a persisted milestone execution record independent of the active task
  pointer;
- a resumable status for failed, interrupted, timed-out, and cancelled task
  executions;
- a task decision that distinguishes `Done`, `Running`, `Failed`,
  `HumanRequired`, `Pending`, `Approved`, and `Abandoned`;
- explicit approval before a HumanRequired continuation;
- a stable execution key composed of project, milestone, task, execution, and
  attempt identifiers;
- idempotent artifact and checkpoint operations keyed by that execution key;
- recovery APIs that preserve prior findings and artifacts instead of replacing
  them;
- bounded retry metadata separate from process recovery metadata.

## M20 acceptance mapping

The requested six-task fixture, deterministic failures, temporary state/run
directories, fake providers, artifact chain, checkpoint recorder, retry limits,
crash simulation, timeout, cancellation, abandonment, and idempotent
re-execution must be added only after that generic contract is available.
Until then, reporting those scenarios as validated would be misleading.

No AWS provider, AWS profile, network call, push, merge, checkpoint, or commit
was used by this audit.
