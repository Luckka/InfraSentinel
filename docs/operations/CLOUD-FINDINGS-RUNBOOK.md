# Cloud Findings Gate Runbook

## Scope

This runbook covers offline evaluation of the M19 `cloud-security-findings-gate` milestone. It does not authorize AWS mutation, deployment, Terraform apply, secret access, customer-data access, or automatic remediation.

## Normal execution

1. Use the existing M18 normalized snapshot or a sanitized fake/offline provider.
2. Run the milestone through `EngineHost`; do not invoke a parallel runner or state machine.
3. Review `.ai-runs-infrasentinel/cloud-findings-gate.json`.
4. Check findings, evidence, limitations, recommendations, review status, approval status, and checkpoint status.
5. Treat `Unknown` and `Blocked` as non-approvable until evidence is repaired and the gate is rerun.

The milestone tasks are FINDINGS-001 through FINDINGS-010: load snapshot, evaluate security, resilience, observability, cost/governance, aggregate, review, approval boundary, persist evidence, and validate the semantic checkpoint.

## Interpretation

`Pass` means the rule has sufficient evidence and no violation was observed. `Finding` means a violation or cost/governance concern was observed. `Unknown` means the evidence is insufficient; it is not safe. `NotApplicable` means the resource does not belong to the rule’s scope. `Blocked` means the snapshot cannot support evaluation.

Critical findings always require `HumanRequired`. High findings require review and an explicit remediation recommendation or approval decision. Medium findings remain in the artifact. Low findings are informational. Recommendations are deliberately descriptive only: restrict exposure, enable encryption/backup/logging, assign ownership, review paid services, or restrict regions. M19 never executes them.

## Live AWS boundary

M19 does not execute live AWS calls by default and no live call is required for the tests. If a future compatibility check is approved, it must use only `personal-infrasentinel`, the previously authorized account, the authorized regions, and the M18 read-only allowlist. Record profile, regions, services, operations, call count, result, and the explicit absence of mutations. Never print or persist credentials, secrets, full payloads, bucket/database contents, function code, or customer data.

## Failure and recovery

For `Unknown`, identify the named limitation, repair the observation input, and rerun offline. For `Blocked`, stop approval and repair collection status. For review failure, inspect the artifact and correct the local implementation or fixture. For checkpoint failure, do not force a commit; preserve the artifact for diagnosis and rerun through EngineHost after the boundary is satisfied.

To stop execution, cancel the host operation. There is no AWS rollback because M19 has no mutating path. Local rollback is a normal Git revert of the M19 commits after review; do not delete artifacts or source files blindly.
