# ADR-0005 — Local IAEngine package consumption

Status: **Blocked pending IAEngine package-boundary approval**

## Context

InfraSentinel currently consumes the IAEngine through a reviewed local
`ProjectReference`. M5 proposes replacing that source dependency with a local
NuGet package.

## Current finding

The IAEngine project is still an executable monolith. A package produced from it
would also contain historical OnlineOS, Flutter, Patrol and ADB compatibility
implementation. That is not yet a safe technology-neutral package boundary.

## Decision

Do not change the consumer project in this blocked stage. Keep the
`ProjectReference`, validators, policies, fakes and project-owned persistence
unchanged. No package restore or `.nupkg` consumption is claimed.

The next implementation step requires approval of either a dedicated reusable
Engine assembly or an explicit temporary risk waiver for a monolithic package.

