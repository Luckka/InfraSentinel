# ADR-0013: Read-Only Cloud Observation

## Status

Accepted for synthetic local validation

## Decision

Represent cloud observations with neutral snapshot contracts and validate them against the existing IaC model before any external provider is introduced. Use synthetic fixtures and a read-only MCP boundary as the only M16 sources.

## Rationale

This proves the observation, comparison, evidence, approval, and checkpoint boundaries without granting cloud access or introducing an external SDK. Unknown evidence is explicit and fail-closed.

## Consequences

- The provider cannot mutate infrastructure by contract.
- The artifact is reproducible and scoped to the InfraSentinel run directories.
- A future AWS adapter must remain outside IAEngine.Core and must pass a new human-reviewed security design.
- M16 does not claim real AWS coverage or runtime drift completeness.
