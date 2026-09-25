# M9 — Resilience Contract Gate

Status: local deterministic proof, proposed and not automatically approved.

## Initial state and reused decisions

M8 already proved that InfraSentinel can own domain rules and adapt them to the
existing IAEngine `IValidationRunner` contract. `EngineHost`, the Orchestrator
validation stage, retry/recovery state, run persistence, milestone state and
explicit checkpoint approval were reused unchanged. The Architecture Defense
Gate remains independent and is not folded into this gate.

No public IAEngine contract was changed. The IAEngine Core still owns workflow
orchestration, state transitions, generic persistence, recovery, review and
approval mechanics. InfraSentinel owns the resilience model, rules, severities,
findings and recommendations.

## Model

`ResilienceContractFixture` contains a stable fixture id, version, execution id
and one or more `ResilienceComponent` records. A component declares:

- service and operation type;
- external dependencies and criticality;
- explicit dependency timeouts;
- finite retry policy, backoff and retryable error classes;
- repeatability and an idempotency strategy;
- recovery strategy and failure destination/DLQ equivalent;
- observability signals;
- explicit classification and handling for temporary, timeout, authentication,
  validation, definitive and unknown errors;
- contract status, evidence, severity and human-approval requirement.

The model is owned by `InfraSentinel.Core.Resilience` and is not part of
`IAEngine.Core`. It contains no Flutter, Patrol, ADB, AWS, provider, secret or
external endpoint concepts.

## Deterministic rules and severities

| Rule | Condition | Severity | Approval behavior |
| --- | --- | --- | --- |
| `timeout-required` | External dependency lacks a positive timeout | High | `HumanRequired` |
| `retry-limited` | Retry policy is infinite, lacks backoff/classification, or retryable errors have no finite policy | Critical for infinite retry, otherwise High | `HumanRequired` |
| `idempotency-required` | Repeatable operation lacks idempotency | High | `HumanRequired` |
| `recovery-required` | Async/distributed operation lacks recovery strategy | High | `HumanRequired` |
| `failure-destination-required` | Repeatable or async/distributed operation lacks DLQ/quarantine/equivalent | Medium for low-severity component, High otherwise | `HumanRequired` |
| `observability-minimum` | No declared log, metric, trace, correlation id or alarm | Medium | `HumanRequired` |
| `error-classification-explicit` | Required error class is missing, duplicated/conflicting, unhandled, or retry policy retries authentication/validation/definitive errors | High | `HumanRequired` |
| `critical-dependency-behavior` | Critical dependency lacks outage behavior | High | `HumanRequired` |
| `contract-approved` | Contract status is Draft, Proposed or Rejected | High | `HumanRequired` |
| `human-approval-boundary` | Component explicitly requires human approval | Critical | Never auto-approved |

The validator is pure for a given fixture. Findings are ordered by rule
evaluation and component declaration order. No current rule uses wall-clock
time, random values or external state.

## Workflow and artifacts

```text
ResilienceContractFixture
  -> ResilienceContractValidator
  -> ResilienceValidationRunner
  -> IAEngine IValidationRunner
  -> EngineHost / Orchestrator validation stage
  -> resilience-contract.json
  -> Approved or HUMAN_REQUIRED
```

The runner writes `resilience-contract.json` under
`.ai-runs-infrasentinel/`. The Engine separately persists its run and validation
artifacts through the existing `RunStore`; milestone state uses
`.ai-state-infrasentinel/`. Generic `.ai-runs` and `.ai-state` are not used.

The artifact includes execution id, fixture id/version, components, rules
executed and passed, findings, severities, evidence, recommendations, status,
human-approval requirement and justification. The execution id is supplied by
the local fixture, so repeated evaluation of the same fixture remains logically
deterministic. The Engine's actual run id remains in its normal run artifact.

## Milestone

`resilience-contract-validation` contains five ordered tasks:

1. `RESILIENCE-001` — Load resilience contract fixture
2. `RESILIENCE-002` — Validate timeout and retry policy
3. `RESILIENCE-003` — Validate idempotency and recovery
4. `RESILIENCE-004` — Validate observability and failure routing
5. `RESILIENCE-005` — Produce explainable resilience result

A valid fixture completes all tasks, reaches `CompleteAwaitingApproval`, and
requires explicit `ApproveMilestoneAsync` to become `Approved`. An invalid
fixture reaches `HUMAN_REQUIRED` through the existing validation failure and
recovery policy. A recovery test proves that a later passing validation can
continue the workflow without changing IAEngine behavior.

## Scope limits and deferred decisions

- Fixtures are local declarations; no live dependency is contacted.
- No AWS, Kubernetes, database, API, cloud, network or external provider access
  is implemented.
- The gate does not prove that a declared timeout is correctly configured in a
  runtime; it checks that the contract declares one.
- Evidence is declarative local evidence and is not independently attested.
- No automatic severity override or LLM interpretation is allowed.
- A future milestone may define evidence retention, redaction, schema evolution
  and a human-facing approval command.

## Learning Checkpoint

1. A timeout is mandatory because an unbounded dependency can consume workers,
   connections and retry budget indefinitely.
2. Retrying without idempotency can repeat a side effect after a timeout even
   when the first attempt actually succeeded.
3. Retryable errors are expected to recover with bounded repetition; non-retryable
   errors need rejection, correction, quarantine or human handling.
4. A provider timeout is not automatically a definitive failure because the
   remote operation may have completed even though its response was lost.
5. A DLQ or quarantine destination preserves failed work for inspection and
   controlled replay instead of silently dropping it.
6. Reconciliation is necessary when local and remote state can diverge after a
   partial success or lost response.
7. Logs, metrics, traces, correlation ids and alarms provide evidence of where,
   how often and for how long a failure occurred.
8. These rules belong to InfraSentinel because they are project/domain policy;
   IAEngine should execute the policy without embedding its meaning.
9. The result becomes `HUMAN_REQUIRED` whenever a required invariant is missing,
   a finding remains, or a component explicitly requires human approval.
10. Another project can supply a different fixture, validator and runner through
    the same generic `EngineHost` while IAEngine continues to own workflow,
    state, persistence, recovery and approval.
