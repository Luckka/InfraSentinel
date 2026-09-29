# M18 — Real AWS Read-Only Observation

## Status

Implemented on `feature/m18-real-aws-read-only-observation`. The default path
is fake/offline. Real AWS access is opt-in only and is not enabled by default.

## Flow

```text
EngineHost
  -> real-aws-read-only-observation milestone
  -> AWS observation provider
  -> exact read-only policy
  -> AWS SDK for .NET
  -> sanitized neutral snapshot
  -> IaC comparator
  -> deterministic validation/evidence
  -> review
  -> explicit human approval
  -> existing local semantic checkpoint
```

IAEngine remains the generic orchestrator. InfraSentinel owns the provider
ports, AWS policy, normalizer, comparator input, evidence interpretation, and
milestone declaration. No parallel executor or second state machine was added.

## Ports and adapters

The cloud boundary exposes small, technology-neutral ports:

- `ICloudObservationProvider` — existing observation port consumed by the
  validation runner;
- `IAwsReadOnlyPolicy` — exact operation, profile, account, region, and
  credential-material policy;
- `IAwsIdentityVerifier` — validates STS identity before resource calls;
- `ICloudSnapshotNormalizer` — converts sanitized DTOs to the neutral cloud
  snapshot;
- `ICloudEvidenceWriter` — evidence persistence port.

`AwsSdkReadOnlyClient` is the infrastructure adapter. AWS SDK types do not
cross into the neutral snapshot or IaC domain. `FakeClient` and
`SyntheticCloudObservationProvider` are used by normal tests.

## Authorized scope

- Profile: `personal-infrasentinel` only.
- Account: configured through `INFRA_SENTINEL_AWS_ACCOUNT_ID` and matched
  against STS before observation.
- User: the STS ARN must end with `/infra-sentinel`.
- Regions: `INFRA_SENTINEL_AWS_REGIONS`, defaulting to `us-east-1`.
- Default live state: disabled.

Live access requires both:

```text
INFRA_SENTINEL_ENABLE_LIVE_AWS=true
INFRA_SENTINEL_AWS_DRY_RUN=false
```

The SDK resolves the named profile directly through the AWS credential
profile chain. Runtime does not shell out to AWS CLI and never serializes
credential material.

## Allowlist

Only these operations are implemented:

- STS `GetCallerIdentity`;
- EC2 `DescribeInstances`, `DescribeAddresses`, `DescribeNatGateways`,
  `DescribeSecurityGroups`;
- RDS `DescribeDBInstances`;
- Lambda `ListFunctions`;
- S3 `ListBuckets`, `GetBucketLocation`, `GetBucketEncryption`,
  `GetPublicAccessBlock`;
- ECS `ListClusters`, `ListServices`;
- EKS `ListClusters`.

Create, Put, Update, Delete, Modify, Terminate, Attach, Detach, network/IAM
changes, Secrets Manager, Parameter Store, object contents, database contents,
function code, logs, queues, and customer data are outside the adapter. An
unknown operation or a credential-like parameter is rejected fail-closed.

AccessDenied is converted into a sanitized limitation. It never triggers a
profile fallback or a second account attempt.

## Resilience and determinism

- Each AWS SDK client disables its SDK-level retry and the adapter applies a
  maximum of three bounded attempts.
- A linked `CancellationToken` and configured timeout cover identity and
  observation calls.
- Caller cancellation is rethrown; an internal timeout becomes an explicit
  timed-out observation result.
- Resources, services, operations, limitations, and findings are sorted before
  normalization or serialization.
- Snapshot timestamps are fixed to `UnixEpoch` for deterministic artifacts.
- Account identity is represented in evidence by a short SHA-256 account hash.

## Neutral snapshot and evidence

AWS responses are projected into `CloudResourceSnapshot` and include only
sanitized properties such as resource type, state, engine, runtime, and public
exposure indicators. Raw SDK response objects, IP addresses, secret values,
tokens, bucket contents, and customer data are not serialized.

Evidence is written below `.ai-runs-infrasentinel/` by the existing
`CloudObservationArtifactWriter`. M18 metadata records the account hash,
regions, services, observed operation names/outcomes, failures, retries, and
timeout state. Review, approval, and checkpoint fields remain controlled by
the existing EngineHost flow; `pushPerformed` and `mergePerformed` remain
false.

## IaC comparison

M18 reuses `IacCloudSnapshotComparator`. Missing or unexpected resources,
public exposure, encryption, logging, backup, owner, region, environment,
dependency, cost-risk indicators, and unknown limitations are not silently
approved. Unknown evidence becomes a limitation/finding requiring review.

## Testing

Normal tests use no AWS network call and cover profile/account/region scope,
mutating and unknown operations, credential-like input, timeout,
cancellation, bounded retry, AccessDenied, sanitization, deterministic
normalization, empty/divergent snapshots, disabled live mode, and the M18
milestone declaration.

`AwsReadOnlyLiveTests` is separate and is skipped unless
`INFRA_SENTINEL_ENABLE_LIVE_AWS=true`; it still requires the authorized
profile, account id, and `INFRA_SENTINEL_AWS_DRY_RUN=false`.
