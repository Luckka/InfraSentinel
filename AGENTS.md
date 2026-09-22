# InfraSentinel contribution instructions

- Use Conventional Commits.
- Keep commits small, logically coherent, and testable.
- Do not create empty, cosmetic, or history-rewriting commits.
- Do not add credentials, customer data, real endpoints, private logs, or generated
  artifacts.
- Do not run scanners, exploit tests, stress tests, AWS calls, or provider agents
  against external systems without an explicit human-approved scope.
- Record relevant verification commands and architectural decisions.
- Keep IAEngine as a separate repository until a reviewed generic integration
  contract exists; do not add a fictitious project reference or copy its code.

These are repository work instructions, not runtime configuration.
