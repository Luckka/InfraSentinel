# M12 — Security Invariant Gate

## Objective and ownership

M12 adds the first local security capability to InfraSentinel. The fixture,
security domain, findings, severities and remediation recommendations belong to
InfraSentinel. IAEngine.Core remains responsible for the generic workflow:
milestones, task ordering, validation, review, bounded remediation, approval,
persistence and Git checkpoint policy.

No AWS, scanner, provider, external infrastructure or real data is used.

## Milestone

`sentinel-security-invariant-gate` is supplied by the existing
`IEngineMilestoneSource` and contains dependent tasks:

```text
SECURITY-001 → SECURITY-002 → SECURITY-003 → SECURITY-004 → SECURITY-005 → SECURITY-006
```

The existing `MilestoneRunner` and `EngineHost` execute the graph. No parallel
executor or state machine was introduced.

## Rules

| Rule | Finding severity | Human approval |
|---|---:|---:|
| Sensitive or critical resource publicly exposed | High/Critical | Critical cases |
| Sensitive data without explicit encryption | High | Yes |
| Excessive permission for declared identity purpose | High | Yes |
| Plaintext secret in fixture | Critical | Always |
| Critical resource without audit logging | High | Yes |
| Critical dependency without owner and outage behavior | High | Yes |
| Any Critical finding | Critical boundary | Required |

Every finding contains rule id, resource id, severity, title, explanation,
evidence, remediation suggestion, deterministic status and approval requirement.

## Fixtures

- Safe fixture: all invariants pass and validation returns `Pass`.
- Correctable fixture: the first deterministic validation fails, then a bounded
  remediation sequence supplies a passing result and the milestone reaches
  `CompleteAwaitingApproval`.
- Critical fixture: plaintext secret produces `Critical` and `HumanRequired`;
  no checkpoint is requested and no commit is created.

## Artifact

The runner writes:

```text
.ai-runs-infrasentinel/security-invariant-gate.json
```

The artifact records execution, milestone/task, fixture version, rules, findings,
evidence, remediation, status, approval status, human approval requirement and
commit status. Generic `.ai-runs` and `.ai-state` directories are not used.

## Approval and checkpoint

The safe fixture reaches `CompleteAwaitingApproval` after the EngineHost executes
the milestone. Only `EngineHost.ApproveMilestoneAsync` causes a fresh checkpoint
request. The M11-C `InfraSentinelGitCheckpointCoordinator` then re-reads Git state,
applies `GitCheckpointPolicy`, creates a semantic local commit and returns its SHA.
Push and merge remain false.

`HumanRequired`, failed validation, incomplete milestone, invalid diff, unrelated
files, invalid branch, invalid semantic message and critical findings block the
checkpoint.

## Recovery and continue

Bounded validation remediation is covered by the milestone integration test.
`ContinueMilestoneAsync` remains the existing IAEngine behavior and was not
modified. A separate artificial Continue scenario was not introduced because
that would duplicate or alter the Engine state machine; the current workflow
resolves the supplied review fixture inside the run when remediation is enabled.
This remains a follow-up validation item before declaring the milestone fully
complete.

## Relationship to earlier gates

- Architecture Defense validates whether architectural decisions are documented,
  evidenced and approved.
- Resilience Contract validates timeout, retry, recovery, failure routing and
  observability declarations.
- Security Invariant Gate validates local security properties over resources,
  identities, permissions, secrets, logging and critical dependencies.

All three are consumer-owned rule sets executed by the same generic EngineHost.

## SOLID and Clean Architecture review

The validator is pure and deterministic; the runner only adapts and persists; the
EngineHost orchestrates; the generic policy decides checkpoint eligibility; and
the Sentinel coordinator controls local Git operations. IAEngine.Core contains no
security rule or Sentinel model, and InfraSentinel references only IAEngine.Core.

## Limitations and deferred decisions

- Fixtures are synthetic and do not prove security of a deployed environment.
- No provider or external scanner is consulted.
- Timestamp values are runtime values rather than deterministic fixture data.
- Continue behavior is not extended in M12.
- More detailed identity/resource graphs and policy-as-code integration are
  deferred to a future milestone.
