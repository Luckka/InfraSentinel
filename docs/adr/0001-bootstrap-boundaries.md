# ADR 0001 — Bootstrap Boundaries

Status: proposed

## Context

InfraSentinel is being started as an independent repository. Its first commit must
not imply that infrastructure scanning, AWS access, financial validation, or AI
runtime integration has been designed or approved.

## Proposal

Keep the bootstrap local and deterministic: a .NET solution, synthetic fixtures,
tests, and documentation only. Keep the IAEngine repository separate until the
Engine exposes and proves a generic runtime composition contract.

## Consequences

This repository is safe to evolve without access to customer systems, but it does
not yet provide production checks or an IAEngine consumer adapter.

## Open decisions

- Which infrastructure signals are in the first approved scope?
- What threat model and authorization evidence are required for each adapter?
- Which deterministic check result contract should be shared with an orchestrator?
- Which human gates are mandatory before any external integration?
