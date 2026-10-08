# ADR 0020: Reconstruct the roadmap from executable evidence

## Status

Accepted for the local M24 audit slice; no external or production approval is
implied.

## Context

The Markdown roadmap had historical milestones and proposals but no reliable
executable next task. M22's documentation conflicted with its ADR, and the
generated Work Loop prompt reported stale repository state. Branch and commit
history show that the project already contains implemented local gates, adapters
and tests, but completion status must be based on code and validation evidence.

## Decision

Maintain a reconstructed roadmap with explicit states: `COMPLETED`,
`PARTIALLY_COMPLETED`, `DOCUMENTED_ONLY`, `BLOCKED` and `NOT_STARTED`. M24 is a
small consumer-only test slice for terminal recovery through the existing
`EngineHost.RecoverMilestoneAsync` contract. The IAEngine repository remains
read-only and no second state machine is introduced.

## Consequences

- M20 is not retroactively marked complete.
- M22 cannot be marked complete until the consumer integration test passes.
- M23 remains partial until the Work Loop can derive a safe next task.
- Live AWS and all external infrastructure remain out of scope.
