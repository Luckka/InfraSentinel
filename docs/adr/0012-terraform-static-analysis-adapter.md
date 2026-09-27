# ADR 0012 — Terraform Static Analysis Adapter

## Status

Accepted for M15.

## Context

InfraSentinel needs IaC security and resilience findings without executing
Terraform or connecting to AWS. The M14 project adapter boundary already
provides bounded, read-only adapter selection and a generic validation entry
point.

## Decision

Add a Terraform adapter implementing the existing `IProjectAdapter` contract.
The adapter parses a documented, conservative subset of HCL-like Terraform
blocks into InfraSentinel's `NeutralIaCModel`. A separate deterministic
validator applies IaC-specific rules and emits source-aware findings. Adapter
analysis and validator results are persisted in
`.ai-runs-infrasentinel/terraform-static-analysis.json` through the existing
EngineHost workflow and checkpoint coordinator.

The adapter fails closed for missing/unsupported workspaces, invalid syntax,
limits, cancellation, and unsafe paths. Unknown expressions and external module
contents become limitations rather than guessed values.

## Consequences

Positive:

- Terraform knowledge remains outside IAEngine.Core;
- no Terraform/AWS execution is required;
- findings are deterministic and explainable;
- critical findings stop at `HumanRequired`;
- existing approval and checkpoint boundaries are reused.

Trade-offs:

- this is not complete HCL support;
- static literals cannot prove runtime behavior;
- dynamic expressions require future provenance support;
- conservative limits may reject large workspaces.

## Alternatives rejected

- Running `terraform plan` or `terraform init`.
- Calling AWS or an external scanner.
- Adding IaC rules to IAEngine.Core.
- Treating unknown expressions as safe values.
- Creating a second adapter registry or workflow executor.

## Verification

M15 tests cover selection, safe/insecure/critical/invalid fixtures, source
findings, deterministic results, secrets, limits, traversal, EngineHost,
approval, checkpoint artifact, and no push/merge behavior.
