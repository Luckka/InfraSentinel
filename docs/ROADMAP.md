# InfraSentinel roadmap

ROADMAP_RECONSTRUCTION_REQUIRED=false

This roadmap was reconstructed on 2026-10-07 from the current repository,
tests, Git history, branch topology and the read-only IAEngine checkout. A
document or branch is not treated as completion evidence by itself. Completion
requires implemented code, relevant tests and a successful local .NET 10 build.

## History — confirmed implemented

| Milestone | State | Evidence | Limitations |
| --- | --- | --- | --- |
| Bootstrap/M1–M7 — local consumer foundation | COMPLETED | `2c16246`, `eaa5c04`, `fadcb3b`, `5e70bf5`, `b2d6234`, `9fea57c`; local solution, host, validator, boundary and package tests | Synthetic/local only; historical numbering is reconstructed from commits and ADRs. |
| M8 — Architecture Defense Gate | COMPLETED | `9b1a841`, `e78d1fe`, `3a7c20b`; `ArchitectureDefenseTests.cs` | Declarative fixtures do not grant architectural approval. |
| M9 — Resilience Contract Gate | COMPLETED | `33fbc54`, `dedc3ae`, `2ea941a`; `ResilienceContractTests.cs` | Contract evidence is local and declarative. |
| M11-C — Engine-controlled checkpoint | COMPLETED | `f91d267`, `c176cd9`, `a55f053`; `CheckpointIntegrationTests.cs` | Local commit only; no push or merge from the coordinator. |
| M12 — Security Invariant Gate | COMPLETED | `dafaf23`, `6c4a8ea`, `fcd59da`; `SecurityInvariantTests.cs` | Synthetic fixtures only. |
| M13 — Observability Evidence Gate | COMPLETED | `919bf96`, `e200385`, `4680c9e`; `ObservabilityEvidenceTests.cs` | Evidence is not external attestation. |
| M14 — Project Adapter Boundary | COMPLETED | `306d7fd`, `084ee87`, `19f1923`; `ProjectAdapterBoundaryTests.cs` | Reads supported manifests; does not execute project commands. |
| M15 — Terraform Static Analysis | COMPLETED | `3a0f1e3`, `1f43368`, `3125609`; `TerraformStaticAnalysisTests.cs` | Documented Terraform subset; no Terraform CLI or provider execution. |
| M16 — Read-only cloud observation contract | COMPLETED | `ce22a7f`, `78260e6`; `CloudObservationTests.cs` | Synthetic observation only in this audit. |
| M17 — Cloud provider safety boundary | COMPLETED | `b3d2182`, `e5dede5`, `d934ba8`; `CloudProviderSafetyTests.cs` | Safety policy is local; no provider call was made. |
| M18 — Real AWS read-only observation | PARTIALLY_COMPLETED | `30c6079`, `d709663`, `3729ae0`; adapter and opt-in test exist | Live observation was not executed and remains out of scope. |
| M19 — Cloud findings gate | COMPLETED | `00f0350`, `d7194dc`, `e116a3b`; `CloudFindingsGateTests.cs` | Uses sanitized/local snapshots; no live AWS evidence. |
| M21 — Finding catalog projection | COMPLETED | `830faf6`; `FindingCatalogTests.cs`, ADR 0018 | Projection preserves source findings; it does not own workflow state. |

## In progress

| Milestone | State | Evidence | Remaining work |
| --- | --- | --- | --- |
| M20 — Recovery continuation | BLOCKED / DOCUMENTED_ONLY | `84854de`, M20 architecture note, ADR 0017 | Historical contract gap is documented; no M20 claim is made until a reviewed generic contract is consumed. |
| M22 — Generic recovery integration | PARTIALLY_COMPLETED | `e65718d`, `e773709`, `RecoveryIntegrationTests.cs`, `CheckpointIntegrationTests.cs`; IAEngine exposes `EngineHost.RecoverMilestoneAsync` | Consumer tests cover persistence and generic recovery identity, but do not yet prove terminal milestone reopen through `RecoverMilestoneAsync`. |
| M23 — Local Codex Work Loop consumer | PARTIALLY_COMPLETED | `fdaf679` through `c1aad9a`, `automation/codex-work-loop.json`, work-loop docs | Consumer configuration exists, but Markdown roadmap parsing produces `human-review` instead of a safe executable next task; generated prompt was stale. |

## Blocked

- M20 remains blocked by the historical contract gap recorded in ADR 0017; the
  current IAEngine recovery contract is consumed incrementally by M22, not used
  to retroactively claim M20 complete.
- Any live AWS observation, provider execution, Terraform execution, MCP call or
  OnlineOS integration is blocked by repository policy and this execution scope.
- A generic contract change in IAEngine or any architecture decision requires
  human review before another automated cycle.

## Next milestones

### M24 — Validate M22 terminal recovery in EngineHost — COMPLETED LOCALLY

Objective: validate deterministic terminal, timeout, cancellation, crash/restart,
HumanRequired, and idempotent recovery through `EngineHost`, proving that the
existing milestone runner resumes without a Sentinel state machine.

Problem: M22 documentation says terminal continuation is integrated, but the
InfraSentinel test suite currently proves only recovery metadata and a generic
recovery attempt, not the terminal EngineHost reopen path.

Scope: local integration tests in `CheckpointIntegrationTests.cs`, generic host
idempotence, roadmap/status/runbook evidence, and local validation. Use the
existing synthetic checkpoint milestone and local Git repository helpers.

Out of scope: IAEngine changes, new runtime contracts, new state machines,
AWS/AWS CLI, Terraform, MCP, providers, OnlineOS, push/merge automation inside
the product, and production infrastructure.

Approval criteria: the test proves explicit recovery approval is required, the
EngineHost recovery API resumes the persisted run, the milestone reaches the
expected local terminal state, and the full build/test/diff checks pass.

Current result: the IAEngine recovery-idempotence revision restores persisted
milestone state to `Running`; consumer tests reach `CompleteAwaitingApproval`
after timeout, cancellation, crash/restart, and explicit HumanRequired approval.
They prove original run identity, preserved validation artifacts, no task replay,
stable recovery-attempt counts, repeated approval without a second commit, and
no premature checkpoint. Live-provider, AWS, and OnlineOS scenarios remain out
of scope.

Commits: `test: validate terminal recovery through EngineHost` and the generic
IAEngine recovery-idempotence fix.

### M25 — Repair consumer Work Loop task derivation — BLOCKED

Candidate only after M24 is validated. Define a safe consumer-side roadmap
representation or reviewed integration contract so the reusable IAEngine loop
can derive executable Markdown milestones without inventing scope. This must
not duplicate the IAEngine state machine or alter IAEngine.Core automatically.

## Out of scope

- AWS live access, corporate profiles, provider agents, Terraform CLI, MCP,
  OnlineOS and external infrastructure.
- Production deployment, merge, force-push, main-branch changes and destructive
  Git operations.
- Unreviewed IAEngine contract changes or copied IAEngine implementation.
