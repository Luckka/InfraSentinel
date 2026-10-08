# M24 — Terminal recovery integration

InfraSentinel consumes the generic IAEngine recovery contract through
`EngineHost`, `StartRecoveryAsync`, `ApproveRecoveryAsync`, and
`RecoverMilestoneAsync`. The consumer supplies only its namespaced stores,
synthetic milestone source, local validators, and checkpoint coordinator.

The integration test creates a deterministic validation failure, persists a
`HumanRequired` run, verifies recovery is rejected without explicit approval,
then approves the exact `ExecutionKey` and recovers through the real host. The
first task keeps its original run id and both validation artifacts remain in the
same run directory. Completed tasks are selected as `Done` and are not replayed;
the milestone reaches `CompleteAwaitingApproval` without creating a commit.

Recovery remains distinct from retry and memory Rewind. The test is local and
synthetic: it does not use AWS, Terraform, MCP, OnlineOS, or external providers.

Validated with .NET SDK 10.0.301:

```text
dotnet build InfraSentinel.sln --no-restore
dotnet test InfraSentinel.sln --no-restore
git diff --check
```

The opt-in AWS live test remained skipped. Timeout, cancellation, process-crash
and live-provider scenarios are not claimed by this integration test.
