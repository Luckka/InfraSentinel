# ADR-0004 — Validate the technology-neutral Engine boundary

Status: Proposed; not automatically approved.

## Context

M4 separates the generic IAEngine workflow boundary from technology-specific
compatibility behavior. InfraSentinel must continue to consume the Engine as a
separate project while keeping infrastructure rules, findings and policies in
this repository.

## Decision proposed

Keep the current local `ProjectReference` as a reversible development
integration. InfraSentinel provides its own composition, deterministic
validator, policy, task/milestone source and project-owned persistence paths.
The IAEngine provides only host, orchestration, state, retry/recovery and
generic registration contracts.

The M4 validation uses local fakes and the existing deterministic validator. It
does not require Flutter, Patrol, ADB, AWS, network access or external agents.

## Boundary evidence

- InfraSentinel does not copy IAEngine sources.
- `DeterministicInfrastructureValidator` remains an InfraSentinel domain
  component and returns InfraSentinel-owned structured findings.
- `EngineHost` is consumed through generic contracts and persists artifacts
  under `.ai-runs-infrasentinel/` and state under `.ai-state-infrasentinel/`.
- The IAEngine main suite no longer includes tests requiring the absent OnlineOS
  Flutter application tree.

## Consequences and limitations

This proves the local boundary, but `ProjectReference` is not yet a distribution
contract. A later milestone must define versioned packaging or another reviewed
reference mechanism. No production scanning, cloud access or security policy
engine is implied by this validation.

