# AWS read-only observation runbook

## Safety preflight

Run from the InfraSentinel repository only:

```bash
unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN AWS_DEFAULT_PROFILE
export AWS_PROFILE=personal-infrasentinel

aws sts get-caller-identity --profile personal-infrasentinel
```

Stop if the account is not `005182472979` or the ARN does not end in
`/infra-sentinel`. Never replace the profile with `default` or a corporate
profile. Do not print credential files or credential values.

Before a live observation, verify the existing cost-safety controls:

```bash
aws budgets describe-budget \
  --account-id 005182472979 \
  --budget-name InfraSentinel-ZeroSpend-Guardrail \
  --profile personal-infrasentinel
aws cloudwatch describe-alarms \
  --alarm-names InfraSentinel-Billing-EstimatedCharges-0.01 \
  --region us-east-1 \
  --profile personal-infrasentinel
aws ce get-anomaly-subscriptions --profile personal-infrasentinel
aws sns list-subscriptions-by-topic \
  --topic-arn arn:aws:sns:us-east-1:005182472979:infrasentinel-cost-safety-alerts \
  --profile personal-infrasentinel
```

The SNS subscription must be confirmed. The budget, CloudWatch alarm, and
Cost Anomaly Detection subscription must exist. If any guardrail is missing,
stop before enabling live AWS.

## Configuration

Required for live mode:

```text
INFRA_SENTINEL_ENABLE_LIVE_AWS=true
INFRA_SENTINEL_AWS_DRY_RUN=false
INFRA_SENTINEL_AWS_PROFILE=personal-infrasentinel
INFRA_SENTINEL_AWS_ACCOUNT_ID=005182472979
INFRA_SENTINEL_AWS_REGIONS=us-east-1
INFRA_SENTINEL_AWS_TIMEOUT_SECONDS=30
INFRA_SENTINEL_AWS_MAX_ATTEMPTS=2
```

Do not put these values in `.env`, the repository, artifacts, or committed
configuration. The account id is scope configuration; AWS credential material
must remain in the local AWS profile store.

Without `INFRA_SENTINEL_ENABLE_LIVE_AWS=true`, the host uses a fake/offline
provider. `INFRA_SENTINEL_AWS_DRY_RUN` must also be false for the real adapter.

## Execution and approval

The milestone is `real-aws-read-only-observation` with tasks AWS-001 through
AWS-007. It must be registered in the existing EngineHost composition with a
validation runner and the existing review/checkpoint adapters. A collected
snapshot does not itself authorize a commit: review and explicit human
approval are still required.

## Interruption and recovery

- Cancel the host operation or its `CancellationToken` to stop observation.
- If timeout or AccessDenied occurs, preserve the limitation and do not try a
  different profile or account.
- If a snapshot is incomplete, treat it as unknown and repeat only after the
  scope/permission issue is reviewed.
- Delete only local, untracked run artifacts during cleanup; never delete AWS
  resources as part of recovery.
- A failed review or checkpoint must leave `pushPerformed=false` and
  `mergePerformed=false`.

## Rollback

M18 has no AWS mutation to roll back. Disable the live environment variables,
remove the local run artifact if appropriate, and revert the local M18 commit
through normal human-reviewed Git procedures. Do not change IAM, networks,
security groups, billing guardrails, or workload resources as rollback.
