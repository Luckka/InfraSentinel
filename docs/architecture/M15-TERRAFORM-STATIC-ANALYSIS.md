# M15 — Terraform/IaC Static Analysis Adapter

## Status

Implemented on `feature/m15-terraform-static-analysis`. The analysis is local,
deterministic, bounded, and read-only. No Terraform command, AWS command,
provider, account, credential, or external infrastructure is used.

## Flow and ownership

```text
Terraform workspace
  → IProjectAdapter / ProjectAdapterSelector
  → NeutralIaCModel
  → TerraformStaticAnalysisValidator
  → EngineHost validation/review/remediation
  → approval
  → local checkpoint coordinator
```

The adapter and IaC rules belong to InfraSentinel. IAEngine.Core remains the
owner of orchestration, state, validation workflow, retries, review, approval,
persistence, and generic checkpoint policy.

## Supported Terraform subset

The `terraform` adapter reads `.tf` files and recognizes these block forms:

- `resource "type" "name"`;
- `data "type" "name"`;
- `variable "name"`;
- `output "name"`;
- `module "name"`.

Within resource blocks it recognizes simple literal attributes for exposure,
ports, encryption, logging, backup/retention, criticality, owner, environment,
permissions, dependencies, and secret-like values. It records logical file
paths and source line numbers.

This is not a complete HCL parser. The implementation does not evaluate
expressions, interpolations, `for_each`, `dynamic` blocks, unknown values, or
external module contents. Such cases become explicit limitations; malformed or
unbalanced blocks fail closed.

## Rules

- `IAC-PUBLIC-SENSITIVE`: sensitive resource exposed publicly — Critical.
- `IAC-ENCRYPTION-REQUIRED`: sensitive resource without explicit encryption — High.
- `IAC-SENSITIVE-PORT`: administrative port 22/3389 open to `0.0.0.0/0` — Critical.
- `IAC-IAM-LEAST-PRIVILEGE`: wildcard IAM action/scope — Critical.
- `IAC-LOGGING-REQUIRED`: critical resource without logging — High.
- `IAC-BACKUP-RETENTION`: critical resource without backup/retention — High.
- `IAC-CRITICAL-OWNER`: critical resource without owner — High.
- `IAC-CRITICAL-DEPENDENCY`: unresolved critical dependency — High.
- `IAC-PLAINTEXT-SECRET`: secret-like plaintext literal — Critical.
- `IAC-PRODUCTION-IDENTITY`: critical resource without explicit production identity when required — Medium.

Each finding contains rule, resource, severity, explanation, source evidence,
file, line, remediation guidance, confidence, and an optional limitation.
Critical findings require human approval and cannot reach an automatic commit.

## Safety limits

The adapter reuses the M14 boundary and therefore applies cancellation,
timeout, file-count and file-size limits, path traversal protection, reparse
point/symlink exclusion, sensitive-directory exclusion, and logical-path
serialization. Secrets are not copied to artifacts. No command execution is
available from the adapter.

## Fixtures and outcomes

- Safe fixture: encryption, logging, backup, owner, restricted network, and
  minimum permissions; validator result `Approved`.
- Correctable fixture: Medium/High findings followed by a bounded reanalysis
  after the fixture is corrected; final result approved for checkpoint review.
- Critical fixture: public sensitive resource, open administrative port,
  wildcard IAM, or plaintext secret; result `HumanRequired`.
- Invalid fixture: unbalanced Terraform block; deterministic failure with no
  approval or checkpoint.

## Artifact

The runner writes:

```text
.ai-runs-infrasentinel/terraform-static-analysis.json
```

The artifact contains execution/project/milestone identity, adapter and parser
version, fixture metadata, neutral resources, files, rules, findings,
severities, evidence, limitations, retry/remediation counters, review,
approval, branch, changed files, checkpoint/commit information, and explicit
false `pushPerformed`/`mergePerformed` flags.

## Limitations and next steps

This milestone is static manifest analysis, not Terraform validation, planning,
provider inspection, or runtime proof. A future milestone may define a fuller
HCL parser, richer expression provenance, module contracts, and schema
versioning, but those changes require separate architectural review.
