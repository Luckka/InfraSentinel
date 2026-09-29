# M19 — Cloud Findings Gate

## Purpose

M19 interprets the neutral cloud snapshot produced by M18 and evaluates it with deterministic, read-only rules. It does not call AWS, change resources, or execute remediation. The gate is a domain component and has no dependency on AWS SDK types.

The flow is:

`snapshot → normalization → deterministic rules → findings → severity → evidence → recommendation → review → human approval → local checkpoint`

The existing `EngineHost`, `MilestoneRunner`, validation, review, approval, and checkpoint contracts remain the orchestration boundary. M19 adds no parallel state machine.

## Domain model

`CloudFinding`, `CloudRuleEvaluation`, `CloudFindingEvidence`, `CloudAffectedResource`, `CloudRemediationRecommendation`, `CloudFindingEvaluationResult`, and the related enums are neutral records in `InfraSentinel.Core.Cloud`.

Each finding carries a rule identifier, affected resource, severity, explanation, sanitized evidence, limitation when applicable, confidence, status, deterministic key, and a remediation recommendation whose automatic execution flag is always false.

The gate uses `Pass`, `Finding`, `Unknown`, `NotApplicable`, and `Blocked`. `Unknown` is never converted to `Pass`; an incomplete snapshot cannot be approved. A failed or timed-out collection is `Blocked`. Critical and high findings require the human approval boundary. No recommendation is executed by M19.

## Rule families

- Security: public exposure, administrative ports, encryption, bucket public-access blocking, least privilege, ownership, production identity, region allowlist, and classification.
- Resilience: Multi-AZ, backups, retention, unavailability behavior, redundancy, dependency ownership/observability, recovery evidence, and single points of failure.
- Observability: logging, metrics, alarms, traceability, operational evidence, and critical-resource monitoring.
- Cost and governance: NAT Gateway, Elastic IP association, RDS, load balancers, potentially paid services, tags, regions, owners, and workload purpose.

Rules only assert a finding when the snapshot has sufficient evidence. Missing evidence produces a bounded `Unknown` finding and limitation.

## Determinism and evidence

Resources are evaluated in stable resource-type/resource-id order. Findings are sorted by severity, rule, resource type, sanitized identifier, and explanation. Evidence artifacts use a fixed JSON shape and contain no credentials, secrets, payloads, customer data, or AWS SDK objects. The artifact records `pushPerformed=false` and `mergePerformed=false`.

The artifact path is `.ai-runs-infrasentinel/cloud-findings-gate.json`. Test writers use temporary workspaces; local execution artifacts are not source-controlled.

## Execution modes and recovery

Offline/fake providers and the existing M18 snapshot are the default test path. A live AWS provider remains opt-in through the M18 configuration and is read-only. M19 itself does not re-run live AWS calls. Any live run must use the authorized personal profile, account, and region allowlist documented by the M18 runbook.

To interrupt a run, cancel the `EngineHost` operation. If collection is incomplete, the gate returns `Blocked` or `Unknown`; review and checkpoint approval must not be inferred. Recovery consists of obtaining a complete sanitized snapshot, rerunning the deterministic gate, reviewing the artifact, and obtaining explicit approval. Rollback means discarding the local artifact or reverting the local commit; no AWS rollback is needed because M19 performs no AWS mutation.
