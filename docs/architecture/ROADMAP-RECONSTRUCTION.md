# Roadmap reconstruction — 2026-10-07

## Evidence method

The reconstruction compares the InfraSentinel Git graph, branches, tracked
source, tests, solution projects and documentation with the IAEngine checkout
in read-only mode. OnlineOS was not accessed. No provider, AWS, Terraform CLI
or MCP operation was executed.

The strongest completion evidence is a coherent implementation commit, a
consumer-owned test, and a successful local .NET 10 build/test. Documentation
alone is classified as `DOCUMENTED_ONLY`, and an implementation without the
corresponding end-to-end evidence is `PARTIALLY_COMPLETED`.

## Current finding

The repository was not empty, but its roadmap was stale and internally
inconsistent. `docs/ROADMAP.md` described M22 as validated while ADR 0019 still
said it was incomplete, and the generated prompt treated the Markdown roadmap
as `unknown` and could report a stale dirty tree. M23 therefore could not safely
derive the next task.

M22 is the highest-priority incomplete slice. The existing code consumes the
generic recovery service and `EngineHost` exposes
`RecoverMilestoneAsync`; InfraSentinel lacked a test proving that terminal
milestone reopen path. A safe local probe for M24 exposed an IAEngine gap:
`MilestoneRunner.RecoverAsync` does not restore the milestone runtime state
before driving the recovered task. M24 is therefore blocked rather than falsely
completed.

## Architectural decision

Keep IAEngine separate and unchanged. Use `IAEngine.Core` contracts through the
existing `ProjectReference`, keep all domain rules in InfraSentinel, and extend
only the consumer test evidence. Do not add a second executor, recovery state
machine, or workaround for missing generic contracts.
