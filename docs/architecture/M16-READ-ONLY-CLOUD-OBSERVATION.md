# M16 — Read-Only Cloud Observation Contract

## Objective

M16 introduces a local, deterministic observation boundary for future cloud integrations. It consumes a neutral IaC model and a synthetic cloud snapshot, compares desired and observed state, records explainable evidence, and uses the existing IAEngine workflow for validation, review, approval, and local checkpointing.

## Boundaries

The observation provider is read-only by construction. Its contract has no create, update, delete, policy mutation, or apply operation. The current provider is synthetic and does not access AWS, the network, Terraform, Docker, credentials, or external MCP.

The MCP boundary is conceptual only. `IMcpReadOnlyToolClient` exposes allow-listed read operations and the synthetic implementation rejects unknown or mutating tool names, redacts secret-like arguments, and records calls.

## Flow

```text
NeutralIaCModel + CloudObservationRequest
  -> SyntheticCloudObservationProvider
  -> CloudInfrastructureSnapshot
  -> IacCloudSnapshotComparator
  -> EngineHost validation/review
  -> approval
  -> checkpoint coordinator
```

The exact milestone identifier is `read-only-cloud-observation-validation`. Its execution kind is normal because the IAEngine now routes by explicit intent rather than milestone text.

## Comparison and outcomes

The comparator reports missing and extra resources, public exposure, encryption, logging, backup, owner, region, environment, dependency, and unknown-evidence differences. Critical or inconclusive evidence is not silently approved. Safe snapshots are `Approved`; correctable drift is `Unknown` until the normal remediation/review flow resolves it; critical evidence becomes `HumanRequired`.

Observation retries are bounded by `MaxAttempts` and capped by the consumer at three attempts. Failed or timed-out observations preserve the reason and do not create a checkpoint.

## Artifact

The artifact is written only to `.ai-runs-infrasentinel/cloud-observation.json`. It records the provider, snapshot, IaC reference, differences, findings, limitations, provider-call evidence, retries, review, approval, checkpoint decision, commit SHA when present, and explicit `pushPerformed: false` and `mergePerformed: false`. Secrets, credentials, absolute machine paths, and unnecessary payloads are excluded.

## Limitations and deferred decisions

- The snapshot is synthetic; no AWS account observation is implemented.
- The parser and IaC model are supplied by earlier local milestones; this milestone does not claim complete Terraform or HCL coverage.
- Unknown cloud evidence requires review rather than being treated as safe.
- Future AWS/MCP integration requires a separate security and authorization decision, least-privilege credentials, audit logging, network controls, and explicit human approval.
