# InfraSentinel recovery operations

Use `IAEngine.Core.Recovery` through `IAEngineConsumerConfiguration` and the
existing `EngineHost`. State is isolated in `.ai-state-infrasentinel/recovery`;
findings remain in `.ai-runs-infrasentinel`.

Use a stable execution key for every synthetic milestone task. Record each
attempt with a sanitized reason, and record artifact/checkpoint identities
before a future operation is authorized. Repeating an identity is idempotent.

When a recovery is `HumanRequired`, stop and approve the exact execution key,
then call the public `EngineHost.RecoverMilestoneAsync` contract. Do not use
memory Rewind as recovery. Do not access AWS or introduce a local executor.
Terminal continuation is supported only through the reviewed IAEngine recovery
contract consumed by InfraSentinel; it is not implemented locally.
