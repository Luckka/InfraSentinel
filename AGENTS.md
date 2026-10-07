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

## M23 local Codex work-loop policy

- Use the reusable loop from the IAEngine repository; keep only consumer configuration here.
- Never access or modify OnlineOS, AWS, Terraform, MCP, providers, or external infrastructure.
- Never change `main`, force-push, merge automatically, or create destructive Git operations.
- Use one coherent slice per cycle, with bounded iterations/files/commits and sequential build/test validation.
- Stop on dirty unexplained state, failed validation, `ENGINE_CONTRACT_GAP`, `HUMAN_DECISION_REQUIRED`, secrets, or protected branches.
- Do not create a second state machine or workaround for a missing IAEngine contract.
- Generated task/status files must not contain credentials, tokens, customer data, or runtime artifacts.
