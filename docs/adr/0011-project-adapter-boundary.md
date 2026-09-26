# ADR 0011 — Project Adapter Boundary

## Status

Accepted for M14.

## Context

InfraSentinel needs to inspect external project workspaces while IAEngine.Core
must remain technology-neutral. Directly adding .NET, Node, Flutter, Terraform,
or AWS logic to the Engine would violate dependency direction and make the
generic workflow consumer-specific.

## Decision

Define `IProjectAdapter` and `NeutralProjectModel` in InfraSentinel. Provide
local .NET and Node.js adapters that inspect bounded manifest files without
executing project commands. Select exactly one adapter, fail closed when there
is no unique match, and adapt successful analysis into the existing
`IValidationRunner` contract.

Adapter-specific evidence is persisted in
`.ai-runs-infrasentinel/project-adapter-boundary.json`. The existing EngineHost,
review, approval, and checkpoint coordinator remain the workflow owners.

## Consequences

Positive:

- technology knowledge stays at the InfraSentinel boundary;
- validators consume one neutral model;
- analysis is deterministic and local;
- unsupported or unsafe workspaces do not silently pass;
- IAEngine.Core and the OnlineOS adapter remain untouched.

Trade-offs:

- manifest analysis cannot prove runtime behavior;
- conservative limits can reject large workspaces;
- marker-based secret detection is intentionally incomplete;
- future adapter types need their own tests and evidence semantics.

## Alternatives rejected

- Adding technology-specific contracts to IAEngine.Core.
- Running `dotnet`, npm, Terraform, Docker, or scanners inside an adapter.
- Copying source models from external projects into InfraSentinel.
- Treating an unsupported project as approved.

## Verification

The M14 adapter tests validate selection, neutral projection, safety limits,
secret protection, determinism, EngineHost execution, approval, checkpoint
artifact creation, and blocked unsupported workspaces.
