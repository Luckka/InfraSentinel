# ADR-0003 — Synthetic milestone execution through IAEngine

Status: Proposed; not automatically approved.

## Context

The first local integration proved a direct synthetic task. The next safe proof
must exercise dependency ordering, per-task artifacts, milestone state, approval,
and deterministic `HUMAN_REQUIRED` behavior.

## Decision proposed

InfraSentinel provides `InfraSentinelMilestoneSource` with three local synthetic
tasks. It passes that source to the IAEngine `EngineHost`; the Engine creates the
existing `MilestoneRunner` and `RoadmapStateStore`. InfraSentinel supplies its own
fake providers, validator, policy, and Git service.

Milestone state uses `.ai-state-infrasentinel`; task run artifacts use
`.ai-runs-infrasentinel`. No external provider or infrastructure access is part of
this proof.

## Consequences

- The consumer proves milestone execution without copying Engine sources.
- A completed milestone still requires explicit checkpoint approval.
- The synthetic source is test infrastructure, not a production scanner or
  security policy.
- A future distribution mechanism must replace the sibling checkout reference.
