# InfraSentinel roadmap

## Bootstrap — curren

- local .NET solution;
- synthetic-only fixtures;
- testable project boundary;
- documented security and IAEngine integration limits.

## M21 — Finding catalog projection

- consolidate already-produced Architecture, Security, Terraform and Cloud findings;
- preserve source, severity, evidence, remediation and human-approval requirements;
- write a deterministic local `finding-catalog.json` artifact;
- keep original gate artifacts and IAEngine lifecycle ownership unchanged.

M20 recovery continuation remains blocked by the generic IAEngine contract gap
documented in [`docs/architecture/M20-RECOVERY-CONTINUATION.md`](architecture/M20-RECOVERY-CONTINUATION.md)
and [`docs/adr/0017-recovery-and-idempotent-continuation.md`](adr/0017-recovery-and-idempotent-continuation.md).

## M22 — Generic recovery integration

- generic execution identity and persisted attempts are supplied by IAEngine.Core;
- InfraSentinel uses the namespaced recovery store through EngineHost;
- recovery attempts, HumanRequired approval, artifact identity and checkpoint
  identity are validated locally without AWS;
- terminal continuation is owned by `EngineHost.RecoverMilestoneAsync` and the
  existing `MilestoneRunner`, not by a Sentinel runner.

Status: local contract and EngineHost integration validated; no external
infrastructure execution is part of this milestone.

## Proposed next steps

- approve a threat model and scope for one local deterministic check;
- define evidence, retention, and redaction rules;
- define human approval gates and an architecture-defense record;
- validate the design against a local fixture before considering any adapter.

These are proposals. No external infrastructure execution is authorized by this
roadmap.
