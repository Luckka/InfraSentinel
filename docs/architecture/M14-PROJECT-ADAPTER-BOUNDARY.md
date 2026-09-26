# M14 — Project Adapter Boundary

## Status

Implemented on `feature/m14-project-adapter-boundary` as a local,
deterministic, read-only analysis boundary.

## Problem

InfraSentinel must analyze more than its own synthetic fixtures without putting
technology-specific knowledge in IAEngine.Core. A .NET project and a Node.js
project expose different manifests, but the validators need a small common
representation rather than C#, npm, Angular, Flutter, Terraform, or AWS types.

## Boundary

```text
external workspace
    → IProjectAdapter
    → NeutralProjectModel
    → InfraSentinel validation
    → EngineHost workflow
    → review / evidence / approval / checkpoint
```

`IProjectAdapter` is owned by InfraSentinel. It identifies a project type,
checks compatibility, reads allow-listed manifests, and returns a neutral model
with analyzed files, ignored files, limitations, and evidence. The adapter does
not execute commands, install dependencies, start applications, or commit Git
changes.

## Neutral model

`NeutralProjectModel` contains only project identity/type/version, components,
declared dependencies, endpoints, queues/topics, databases, storage,
public-resource indicators, encryption/logging configuration indicators,
owners, logical file paths, ignored files, and limitations. Absolute machine
paths and raw file contents are not emitted.

## Implemented adapters

### .NET

`DotNetProjectAdapter` reads `.sln`, `.csproj`, `global.json`, and
`appsettings.example.json`. It extracts project components, target framework,
package references, encryption/logging indicators, and logical evidence paths.
It never runs `dotnet restore`, `dotnet build`, or the application.

### Node.js

`NodeProjectAdapter` reads `package.json` and JSON configuration manifests. It
extracts package identity/version and declared dependencies. It never runs npm,
project scripts, or JavaScript.

Angular, Flutter, and Terraform are intentionally only future fixture targets;
their adapters are not implied by the .NET or Node implementations.

## Selection and failure behavior

`ProjectAdapterSelector` selects exactly one compatible adapter. No match or
ambiguous matches return no adapter and the validation runner produces a
blocked, explainable result. The boundary fails closed for missing workspaces,
unsupported files, invalid manifests, timeouts, and configured limits.

## Read-only safety controls

- no shell/process execution in adapters;
- cancellation and a bounded timeout;
- maximum file count and maximum file size;
- reparse points/symlinks are ignored;
- `.git`, dependency, build, Terraform state, secret, and Sentinel state
  directories are excluded;
- relative paths are rejected if absolute or containing `.`/`..` traversal;
- only logical `/relative/path` values enter the model/artifact;
- simple secret markers produce a Critical finding requiring human review;
- secret values are never included in evidence or artifacts.

## EngineHost integration

The `project-adapter-boundary-validation` milestone has eight dependent tasks.
Its validation runner calls the selector and adapter, then adapts the result to
IAEngine's existing `IValidationRunner`. Review, approval, and checkpoint are
still executed by the existing EngineHost workflow and the Sentinel-owned
checkpoint coordinator. No second runner or state machine exists.

The artifact is written to:

```text
.ai-runs-infrasentinel/project-adapter-boundary.json
```

It includes adapter metadata, logical files, ignored files, limitations,
neutral model, findings, validator/review/approval/checkpoint state, commit
SHA, and explicit false push/merge flags.

## Limitations and deferred decisions

- This is manifest inspection, not semantic compilation or runtime analysis.
- No external scanner, package registry, container, AWS account, Terraform
  state, or provider is accessed.
- Secret detection is conservative marker detection, not a complete scanner.
- Adapter schema evolution and remote evidence retention require a future ADR.
- Angular, Flutter, and Terraform fixture adapters remain deferred.

## Verification

Tests cover valid .NET and Node projects, deterministic results, unsupported
workspaces, size limits, path traversal policy, excluded directories, secret
redaction, EngineHost execution, approval, local checkpoint, and artifact
isolation.
