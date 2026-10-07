# ADR 0019: Consume generic recovery through IAEngine.Core

## Status

Accepted for the incremental M22 integration.

## Decision

InfraSentinel creates the generic `IExecutionRecoveryService` with a
consumer-owned `FileRecoveryStore` rooted at `.ai-state-infrasentinel`.
Recovery records are not duplicated in InfraSentinel domain models. Existing
finding artifacts remain authoritative and are identified through the generic
artifact identity contract.

No AWS, Terraform, OnlineOS, or provider execution is part of this integration.
No second state machine or project-specific recovery method is introduced.

## Limitation

The current incremental contract persists and deduplicates recovery metadata,
but terminal workflow continuation still requires a reviewed generic reopen
operation in the central IAEngine workflow. Until that exists, M22 remains
incomplete.
