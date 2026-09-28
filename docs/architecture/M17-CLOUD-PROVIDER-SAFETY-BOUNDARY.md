# M17 — Cloud Provider Boundary and Credential Safety

## Objective

M17 prepares InfraSentinel for a future read-only cloud provider without making a cloud call. The boundary is local, deterministic, and fail-closed.

## Contract and policy

`ICloudProviderSafetyBoundary` accepts an observation request containing only project identity, operation intent, account/region scope, read-only mode, dry-run state, and an optional credential reference. Credential material is rejected. The policy permits `Describe*` and `List*`; `Get*` requires explicit authorization. Create, Put, Update, Delete, Terminate, Run, Start, Stop, Modify, Attach, Detach, Associate, Disassociate, and unknown operations are denied.

Account and region must be present in the allowed scope. Read-only mode must be explicit. The offline provider is disabled by default, and a non-dry-run request with an enabled provider becomes `HumanRequired` without attempting a real call.

## Offline provider

`OfflineAwsReadOnlyProvider` implements the contract without AWS SDK, network, subprocesses, credentials, or external MCP. It returns deterministic decisions and explicit limitations. Timeout and cancellation are represented and bounded retries are handled by the validation runner.

## EngineHost flow

```text
cloud-provider-safety-boundary
  -> EngineHost tasks
  -> offline provider
  -> safety policy
  -> validation/review
  -> explicit approval
  -> local checkpoint coordinator
```

The IAEngine remains generic. It owns orchestration, validation, review, approval, retry, persistence, and checkpoint policy. The provider safety rules and artifact interpretation belong to InfraSentinel.

## Artifact and safety

The artifact is `.ai-runs-infrasentinel/cloud-provider-safety-boundary.json`. It contains decisions, scope results, credential safety, attempts, and explicit `realCallAttempted`, `mutationAttempted`, `pushPerformed`, and `mergePerformed` values. Credential content, tokens, environment values, personal paths, and payloads are never serialized.

AWS real was deliberately not executed. Before any future account access, a separate human-reviewed design is required for identity, least privilege, account/region allowlists, audit logging, network controls, provider enablement, incident handling, and approval boundaries.

## Limitations

- No AWS SDK or MCP server is installed.
- No real cloud response is collected.
- Operation classification is a conservative name-prefix policy, not an AWS service catalog.
- A future provider must preserve the same read-only contract and remain outside IAEngine.Core.
