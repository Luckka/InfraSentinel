# ADR-0002 — Local IAEngine host integration

Status: Proposed; not automatically approved.

## Context

InfraSentinel needs a reversible proof that a separate project can consume the
IAEngine workflow without copying its sources or activating real infrastructure
providers.

## Decision proposed

Use a local MSBuild `ProjectReference` to the IAEngine checkout, overridable via
the `IAEngineProject` property. Pin the consumed Engine revision in
`docs/integrations/iaengine-consumption.json`. InfraSentinel owns its project
profile, component names, fake validator, fake policy, synthetic task, Git fake,
and project-specific `.ai-runs-infrasentinel`/`.ai-state-infrasentinel` names.

## Safety boundary

The smoke test uses only local fakes and a temporary workspace. It does not run
Claude, Codex, Ollama/Qwen, AWS, scanning, exploitation, stress testing, or
external network calls.

## Consequences

- The two repositories remain separate and versioned independently.
- The local path is intentionally temporary until the public host API stabilizes.
- CI or distribution must later provide an explicit Engine reference rather than
  relying on the sibling checkout layout.
