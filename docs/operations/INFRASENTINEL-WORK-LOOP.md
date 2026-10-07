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

The autonomous prompt is also written to
`docs/operations/generated/NEXT-MILESTONE-PROMPT.md`. It is derived from the
roadmap, status, branch and recent commits; it does not assume a fixed next
milestone.

For a bounded autonomous run with push and PR-link generation enabled:

```bash
tools/codex-work-loop/codex-work-loop.sh \
  --project /Users/luckadenubilasevero/Documents/Projects/InfraSentinel \
  --iterations 20 --auto-push --auto-pr
```

Each validated cycle writes `docs/status/LOOP-<timestamp>.md` and records PR
links in `docs/status/PULL-REQUESTS.md`. `gh pr create` is used only when
`gh auth status` succeeds; otherwise a GitHub branch PR URL is generated.
There is no automatic merge. A real Codex cycle remains subject to the
configured dirty-tree, branch, test, architecture and human-decision gates.
