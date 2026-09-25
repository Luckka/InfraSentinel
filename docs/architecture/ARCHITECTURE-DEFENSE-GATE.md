# Architecture Defense Gate

Status: local deterministic proof, proposed and not automatically approved.

## Objective

The Architecture Defense Gate protects consequential architecture decisions
before they are treated as approved. It evaluates only local
`ArchitectureDecisionFixture` data. It does not scan code, call cloud services,
contact external providers, or decide architectural quality through an LLM.

The domain model and rules belong to InfraSentinel. IAEngine only executes the
existing generic `IValidationRunner` through `EngineHost` and persists the
workflow state.

## Decision model

Each decision contains:

- decision id, title, context, problem and explicit decision;
- alternatives considered and consequences;
- evidence or associated tests;
- ADR reference and ADR status;
- decision status, severity and explicit human-approval requirement.

The validator produces an explainable evaluation with the evaluated rules,
passed rules, findings, final status, justification and human-approval flag.

## Rules

| Rule | Result |
| --- | --- |
| `adr-required` | Missing ADR produces a High finding and human review. |
| `adr-approved` | Draft, Proposed, Missing or Rejected ADRs cannot support approval. |
| `decision-approved` | Draft, Proposed or Rejected decisions cannot pass the gate. |
| `alternatives-required` | A relevant decision without alternatives produces a High finding. |
| `consequences-required` | A relevant decision without consequences produces a High finding. |
| `evidence-required-for-sensitive-decision` | High/Critical decisions without evidence require human review. |
| `human-approval-boundary` | An explicitly sensitive decision is never automatically approved. |

Any finding makes the evaluation `HumanRequired`. The validator never converts
that status to `Approved`.

## Findings and severity

Findings have a stable rule id, decision id, severity, explanation and whether
the specific finding requires human approval. Current missing-record and
incomplete-decision rules use High severity. An explicit human approval boundary
uses Critical severity. Severity is explanatory domain data; it is not an
authorization to take external action.

## Workflow and artifact

The integration path is:

```text
ArchitectureDecisionFixture
  -> ArchitectureDefenseValidator
  -> ArchitectureDefenseValidationRunner
  -> IAEngine IValidationRunner
  -> EngineHost / Orchestrator validation stage
  -> architecture-defense.json
  -> Approved or HUMAN_REQUIRED
```

`architecture-defense.json` is written under
`.ai-runs-infrasentinel/`. Milestone runtime state is written under
`.ai-state-infrasentinel/`; generic `.ai-runs` and `.ai-state` are not used.
The artifact includes the decision id, rules evaluated and passed, findings,
severities, justification, final status and human-approval requirement.

An `Approved` task may complete a milestone, but the existing IAEngine
checkpoint remains `CompleteAwaitingApproval`. Only an explicit
`ApproveMilestoneAsync` transition produces milestone `Approved`. A validation
failure uses the existing Engine recovery policy; the M8 test configuration
sets the validation remediation budget to zero to prove immediate
`HUMAN_REQUIRED` behavior without automatic remediation.

## Examples

Approved: an approved ADR is referenced, alternatives and consequences are
non-empty, evidence names local tests, and `RequiresHumanApproval` is false.
The result is `Approved` and the validation process exits successfully.

Blocked: a decision references a Proposed ADR, has no evidence, or has no
alternatives. The result is `HumanRequired`, with one or more explainable
findings in the artifact. No provider or external system is contacted.

## Limitations and next steps

- ADR contents are represented by fixture metadata; no repository-wide ADR
  parser is implemented.
- Evidence is declarative local fixture data; it is not independently attested.
- There is no AWS, endpoint, exploit, stress or external scanning integration.
- A future milestone may define evidence retention/redaction and a human review
  command, subject to an explicit architecture and security decision.

## Learning Checkpoint

1. The gate belongs to InfraSentinel because it protects InfraSentinel's
   architecture decisions and domain policy; IAEngine is a generic workflow
   executor.
2. IAEngine must not decide whether an architecture is good because that would
   move project-specific policy into the reusable orchestration core.
3. Validation checks deterministic execution evidence; review evaluates a
   change or result; architecture defense checks decision records, alternatives,
   consequences and authority boundaries.
4. Sensitive decisions need human authority because a deterministic rule can
   identify missing evidence but cannot grant organizational authorization.
5. A Proposed ADR yields `HumanRequired`; it is evidence of intent, not an
   approved architectural decision.
6. Another project can use the same `EngineHost` by supplying its own fixture,
   validator, `IValidationRunner`, providers and milestone source while IAEngine
   continues to own orchestration, state, retries, persistence and approval
   mechanics.
