# AWS Cost Safety — InfraSentinel

Status: active guardrails configured for the personal AWS account.

## Scope and identity

- AWS profile: `personal-infrasentinel`
- Validated account: `005182472979`
- Validated IAM identity: `arn:aws:iam::005182472979:user/infra-sentinel`
- AWS CLI calls used for the configuration explicitly selected the profile and removed inherited access-key, session-token, and default-profile variables.
- No credentials, access keys, session tokens, or billing e-mail address are stored in this repository.

The profile is an IAM user, not root. No corporate profile, `default`, IAEngine, OnlineOS, or external workload account was accessed.

## Configured guardrails

### Zero-spend budget

Budget: `InfraSentinel-ZeroSpend-Guardrail`

- Type: monthly cost budget covering the account.
- Limit: USD `0.01`.
- Actual-cost alerts: 0%, 50%, 80%, 100%.
- Forecasted-cost alerts: 0%, 50%, 80%, 100%.
- Delivery: AWS Budgets e-mail notification to the operator-provided address.

The USD 0.01 limit is the smallest accepted positive budget value in this account/API path. It is an alerting threshold, not a hard financial block; AWS may continue to authorize billable activity.

### CloudWatch billing alarm

- Region: `us-east-1` (billing metrics are global but exposed there).
- Alarm: `InfraSentinel-Billing-EstimatedCharges-0.01`.
- Metric: `AWS/Billing / EstimatedCharges`, dimension `Currency=USD`.
- Threshold: USD `0.01`, comparison `GreaterThanOrEqualToThreshold`.
- State at verification: `OK`.
- Action: SNS topic `arn:aws:sns:us-east-1:005182472979:infrasentinel-cost-safety-alerts`.

The SNS e-mail subscription is `PendingConfirmation`. The operator must open the confirmation message sent to the configured address and confirm it. Until then, the alarm and budget exist, but SNS delivery to that address is not active.

Billing alerts must remain enabled in the AWS Billing/CloudWatch billing-preferences console for the account. The alarm was created successfully, but the CLI has no reliable account-wide switch that can be assumed to enable that console preference.

### Cost Anomaly Detection

- Account service monitor available: `Default-Services-Monitor` (dimensional, service dimension).
- InfraSentinel subscription: `InfraSentinel-Cost-Anomaly-Alert`.
- Frequency: daily.
- Threshold: absolute anomaly impact greater than or equal to USD `0.01`.
- Subscription status at verification: confirmed.

The account limit did not allow a second dimensional monitor, so the InfraSentinel subscription uses the existing account service monitor. Anomaly detection identifies unusual spend; it does not prevent charges.

### Budget Action and automatic kill behavior

No Budget Action was attached. A safe action would require a reviewed, approval-gated policy limited to `infra-sentinel`; no automatic deny, root restriction, permanent policy change, or resource deletion is enabled.

The kill switch is intentionally manual:

1. Confirm the alert and account identity.
2. Review the current regional inventory.
3. Stop or remove only the explicitly approved workload resource in its owning service.
4. Re-run the inventory and budget/alarm verification.

No automatic deletion, termination, deny policy, or production deployment is part of this setup.

## Region policy

- Billing control-plane region: `us-east-1`.
- Workload-region allowlist: none currently approved; any future workload region requires human review first.
- The inventory checked every currently enabled standard region and found no regional workload resources.
- No additional region was opted into or provisioned.

## Cost allocation tags

The standard tag set for future InfraSentinel resources is:

| Key | Value |
| --- | --- |
| `Project` | `InfraSentinel` |
| `Environment` | `Lab` |
| `ManagedBy` | `Codex` |
| `CostCenter` | `Portfolio` |

Activation was attempted through Cost Explorer, but the account returned `Tag keys not found` because no matching resource tags are currently registered. No paid resource was created to make the tags appear. When a reviewed resource exists, apply these tags at creation and then activate the keys in Cost allocation tags.

## Resource inventory

Read-only inventory covered all enabled standard regions and the global services below. At verification time, all counts were zero:

- EC2 instances, Elastic IPs, and NAT Gateways;
- RDS DB instances;
- ELBv2 load balancers;
- EKS and ECS clusters;
- Lambda functions;
- S3 buckets and CloudFront distributions;
- OpenSearch domains;
- SageMaker endpoints and notebook instances;
- Bedrock custom models and provisioned model throughputs.

No resources were deleted or modified.

## Verification commands

All AWS commands must retain the explicit profile selection and remove inherited AWS credential variables before execution:

```bash
unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN AWS_DEFAULT_PROFILE
export AWS_PROFILE=personal-infrasentinel

aws sts get-caller-identity --profile personal-infrasentinel
aws budgets describe-budget \
  --account-id 005182472979 \
  --budget-name InfraSentinel-ZeroSpend-Guardrail \
  --profile personal-infrasentinel
aws cloudwatch describe-alarms \
  --alarm-names InfraSentinel-Billing-EstimatedCharges-0.01 \
  --region us-east-1 \
  --profile personal-infrasentinel
aws ce get-anomaly-subscriptions --profile personal-infrasentinel
```

These checks are observational. They do not prove that an e-mail subscription has been confirmed or that a billing alarm can block charges.
