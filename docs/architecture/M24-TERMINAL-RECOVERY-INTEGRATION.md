# M24 — Terminal recovery integration

InfraSentinel consumes the generic IAEngine recovery contract through
`EngineHost`, `StartRecoveryAsync`, `ApproveRecoveryAsync`, and
`RecoverMilestoneAsync`. The consumer supplies only its namespaced stores,
synthetic milestone source, local validators, and checkpoint coordinator.

The integration tests create deterministic timeout, cancellation, crash/restart,
and `HumanRequired` interruptions, then recover through a real `EngineHost`.
The first task keeps its original run id and prior validation artifacts remain
in the same run directory. Completed tasks remain `Done` and are not replayed;
the milestone reaches `CompleteAwaitingApproval` without creating a commit.
Repeating recovery and approval returns persisted state and does not create a
second recovery attempt or commit. Recovery artifact and checkpoint identities
are also deduplicated by the generic recovery store.

Recovery remains distinct from retry and memory Rewind. The test is local and
synthetic: it does not use AWS, Terraform, MCP, OnlineOS, or external providers.

Validated with .NET SDK 10.0.301:

```text
dotnet build InfraSentinel.sln --no-restore
dotnet test InfraSentinel.sln --no-restore
git diff --check
```

The current validation result is 148 passed and 1 skipped opt-in AWS test.

The opt-in AWS live test remained skipped. Live-provider, AWS, and OnlineOS
scenarios are not claimed by this integration test.
