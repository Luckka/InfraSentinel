# ADR 0018: Finding catalog remains a consumer-owned projection

## Status

Accepted for M21 local implementation.

## Context

InfraSentinel has several independent, consumer-owned gates. Their findings are
structured and explainable, but their artifacts use gate-specific schemas. A
demonstrable review workflow needs one deterministic view without moving policy
into IAEngine.Core or inventing a second workflow state machine.

## Decision

Add a consumer-owned `FindingCatalog` projection. It accepts findings already
produced by the Architecture, Security, Terraform and Cloud gates, normalizes
their presentation, deduplicates by a stable source-specific key, and writes
`finding-catalog.json` under `.ai-runs-infrasentinel`.

The catalog does not execute validators, infer missing evidence, approve
findings, call providers, or change EngineHost behavior. `HumanRequired` is
preserved whenever an input finding requires human approval.

## Consequences

- Reviewers get one deterministic, local artifact for cross-gate triage.
- Original gate artifacts remain authoritative and are not replaced.
- Findings from future gates require an explicit adapter into this projection.
- Recovery idempotency is not claimed; the generic IAEngine recovery contract
  remains blocked as documented by M20 and ADR 0017.
