# InfraSentinel local Codex Work Loop

InfraSentinel supplies only `automation/codex-work-loop.json`. The reusable
scripts live in the separate IAEngine repository under
`tools/codex-work-loop`; no runtime assembly dependency is added.

From the IAEngine checkout, generate a read-only plan first:

```bash
tools/codex-work-loop/codex-work-loop.sh \
  --project /Users/luckadenubilasevero/Documents/Projects/InfraSentinel \
  --dry-run
```

The generator writes a contextual task to
`docs/operations/NEXT-CODEX-TASK.md` and a sanitized report under
`docs/status/`. These paths are generated local state and are ignored by Git.

An explicit `--once` invokes `codex exec` with workspace-write and approval on
request. It permits one bounded task, one semantic commit, sequential build and
test validation, and stops on dirty state, protected branches, AWS/profile
state, human decisions, contract gaps or validation failure.

`--iterations N` repeats at most N validated cycles and never exceeds the
configured maximum. There is no unbounded loop. Stop with Ctrl-C; the signal
handler prevents a new cycle from starting.

AWS remains disabled. The loop does not invoke AWS CLI, Terraform, MCP,
providers, OnlineOS, or external infrastructure. `Rewind` and recovery are
not replaced or implemented by this tool.

If the Codex CLI syntax changes, review `codex --help` and `codex exec --help`
before updating the invocation. The current supported mode is `codex exec`.
