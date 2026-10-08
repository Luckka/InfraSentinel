# ADR 0021: Consume generic terminal recovery through EngineHost

## Status

Accepted — 2026-10-07

## Context

The M24 probe showed that InfraSentinel could persist recovery metadata but could
not prove that a terminal milestone was reopened through the real EngineHost.
The missing transition belonged to IAEngine's generic MilestoneRunner contract.

## Decision

InfraSentinel consumes the reviewed IAEngine revision `ddb226e` through its
existing local `ProjectReference`. It records an approval for the exact generic
execution key, then invokes `EngineHost.RecoverMilestoneAsync`. No Sentinel
executor, state machine, or copied IAEngine code is added.

## Consequences

The consumer preserves the existing checkpoint coordinator and sanitized local
artifacts. Recovery can resume the persisted milestone while keeping the
original task run identity. AWS, OnlineOS, Terraform execution, MCP, push, and
merge remain outside this validation.
