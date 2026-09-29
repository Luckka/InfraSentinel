# ADR 0014: AWS cost-safety guardrails

- Status: Accepted
- Date: 2026-09-28
- Scope: InfraSentinel personal AWS account only

## Context

InfraSentinel needs protection against accidental AWS spend while keeping infrastructure creation, deletion, and automatic lockout out of scope. Billing telemetry is account-level, while CloudWatch billing metrics are configured in `us-east-1`.

## Decision

Use layered, alert-first guardrails:

1. A monthly USD 0.01 AWS Budget named `InfraSentinel-ZeroSpend-Guardrail` with actual and forecasted alerts at 0%, 50%, 80%, and 100%.
2. A CloudWatch `EstimatedCharges` alarm in `us-east-1` at USD 0.01 routed to the InfraSentinel SNS topic.
3. A daily Cost Anomaly Detection subscription with an absolute USD 0.01 threshold, attached to the account's existing service monitor because the account monitor limit prevents a second dimensional monitor.
4. An operator-confirmed SNS e-mail subscription. Confirmation remains a human step.
5. A documented region allowlist and mandatory future resource tags.
6. A manual, human-approved kill switch rather than an automatic deny policy or deletion action.

## Alternatives rejected

- Zero-dollar budget: the Budgets API requires a positive amount in this account; USD 0.01 is the smallest accepted value.
- Automatic Budget Action: rejected because a safe, approval-gated policy limited to `infra-sentinel` was not already available and an automatic deny could create an unsafe lockout.
- New workload resources for testing: rejected because they could introduce charges.
- Cost allocation tag activation without registered keys: rejected by the service (`Tag keys not found`); no resource was created solely for tag activation.
- A second anomaly monitor: rejected by the account service limit; the existing service monitor is reused.

## Consequences

These controls provide early warning and human response, but they are not a financial circuit breaker. The budget, alarm, anomaly detector, and SNS/CloudWatch configuration may have service-specific charges or limits, and the billing e-mail subscription must be confirmed. The account remains capable of incurring charges if a user or service creates billable resources.

## Verification record

- Account identity verified as IAM user `infra-sentinel` in account `005182472979`.
- Profile used: `personal-infrasentinel`.
- CloudWatch alarm verified in `us-east-1`.
- Budget and all eight notification thresholds verified.
- InfraSentinel anomaly subscription verified as confirmed.
- SNS subscription verified as pending confirmation.
- Regional inventory found no active resources in the inspected services; no mutation was performed during inventory.
- No credentials or e-mail address were persisted in the repository.
