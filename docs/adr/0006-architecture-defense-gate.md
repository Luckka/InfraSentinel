# ADR-0006 — Local architecture defense gate

Status: Proposed; not automatically approved.

## Context

InfraSentinel needs a first defense boundary for consequential architecture
decisions. The boundary must remain local, deterministic and explainable while
using the existing IAEngine consumer integration.

## Decision proposed

Keep architecture decision models, findings, severities and rules in
`InfraSentinel.Core.Architecture`. Adapt the validator to IAEngine's existing
`IValidationRunner` contract. Use the existing `EngineHost` and milestone
approval flow for orchestration, persistence and `HUMAN_REQUIRED` handling.

## Safety boundary

The implementation evaluates one in-memory fixture at a time. It performs no
network access, cloud access, external scanning, code-client analysis, provider
calls or automatic authorization. Artifacts and milestone state remain in
InfraSentinel-owned directories.

## Consequences

- Architecture policy remains owned by the consuming project.
- IAEngine requires no new public abstraction or namespace change.
- Incomplete or sensitive decisions cannot become automatically approved.
- The fixture's evidence is deterministic but not independently attested.
- Human approval and future evidence retention rules remain open decisions.
