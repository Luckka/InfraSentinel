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

M6 introduces a dedicated local `IAEngine.Core` project without changing to
`PackageReference`. InfraSentinel consumes that project directly and does not
reference the executable or OnlineOS adapter. The package decision remains
deferred until the Core API is stable. No package restore or `.nupkg`
consumption is claimed.
