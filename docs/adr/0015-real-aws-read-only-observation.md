# ADR 0015: real AWS read-only observation

- Status: Accepted
- Date: 2026-09-29
- Scope: InfraSentinel personal AWS account, observation only

## Context

M16 and M17 defined a synthetic cloud snapshot and a fail-closed provider
boundary. The next step is to observe a real AWS account without allowing
InfraSentinel to mutate infrastructure or duplicate IAEngine orchestration.

## Decision

Implement a concrete AWS SDK for .NET adapter behind InfraSentinel-owned
ports. The adapter:

1. accepts only the explicit `personal-infrasentinel` profile;
2. verifies STS account and `/infra-sentinel` user identity before resource
   calls;
3. accepts only the documented read-only operation allowlist;
4. observes only configured regions;
5. disables live AWS by default and requires explicit environment opt-in;
6. uses bounded timeout/retry and caller cancellation;
7. sanitizes SDK responses into neutral snapshots;
8. reuses the existing IaC comparator, EngineHost review, approval, and local
   checkpoint boundary.

## Alternatives rejected

- AWS CLI subprocesses at runtime: rejected because profile and credential
  handling would be less explicit and harder to test.
- AWS SDK types in the domain: rejected to preserve cloud-provider
  substitutability.
- A parallel M18 executor: rejected because IAEngine already owns milestone
  state, retry, review, approval, and checkpoint sequencing.
- Live AWS as the default: rejected because a local test or host invocation
  must not unexpectedly access an account.
- Broad `Describe*`/`List*` prefix matching: rejected in favor of an exact
  allowlist.
- Automatic remediation: rejected because M18 is observation-only and human
  approval remains required.

## Consequences

M18 can produce a real, sanitized snapshot when explicitly enabled, but it is
not a complete AWS inventory and unknown evidence remains reviewable rather
than safe. SDK package dependencies and provider permissions must be reviewed
as the allowlist evolves. Read-only APIs can still incur service-specific API
costs or expose resource metadata, so the account cost guardrails and human
approval boundary remain required.

No live AWS observation was executed during this implementation run. The
preflight identity and cost-safety checks used only the authorized profile and
read-only calls.
