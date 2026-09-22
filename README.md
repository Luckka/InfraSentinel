# InfraSentinel

InfraSentinel is a planned, safety-first project for validating infrastructure
resilience and security with controlled, evidence-producing checks. This repository
is only the initial bootstrap: it does not scan, attack, stress, or connect to
external infrastructure.

## Current scope

- local .NET solution and test project only;
- synthetic fixtures and local examples only;
- no customer data, credentials, real endpoints, AWS integration, financial rules,
  exploit code, or stress testing;
- human approval remains required for architecture, security, and deployment
  decisions.

InfraSentinel does not currently consume the IAEngine runtime. IAEngine remains a
separate repository, and its generic runtime composition must be implemented and
validated before InfraSentinel can become an independent consumer.

## Roadmap

1. Establish the local testable foundation and threat-model boundaries.
2. Propose and approve deterministic check contracts using synthetic fixtures.
3. Add an architecture-defense and decision-record gate before consequential work.
4. Add narrowly scoped adapters only after a human-approved security review.
5. Evaluate IAEngine integration only after its generic composition contract exists.

No item above authorizes testing against systems that InfraSentinel does not own.

## Development

```bash
dotnet build InfraSentinel.sln
dotnet test tests/InfraSentinel.Core.Tests/InfraSentinel.Core.Tests.csproj
```

See [`AGENTS.md`](AGENTS.md) for the commit and review policy. Proposed decisions
are recorded under [`docs/adr`](docs/adr) and are not approvals.
