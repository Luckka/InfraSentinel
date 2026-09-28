# ADR-0014: Cloud Provider Safety Boundary

## Status

Accepted for offline preparation only

## Decision

Introduce an InfraSentinel-owned, fail-closed provider safety boundary with an explicit read-only operation allowlist, account/region scope checks, credential-reference validation, bounded retries, and a synthetic offline provider.

## Rationale

The contract proves safety decisions without installing an external SDK or granting cloud access. Unknown and mutating operations are denied, and real provider mode requires a later human-approved rollout.

## Consequences

- Tests do not need credentials or network access.
- Artifacts can prove that no real call or mutation was attempted.
- `Get*` operations require explicit authorization.
- The operation prefix policy must be replaced or supplemented by a reviewed provider capability catalog before AWS access.
- IAEngine.Core remains unaware of AWS and InfraSentinel rules.
