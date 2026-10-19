---
applyTo: "**"
---

# MediaDock 2.0

Respond only in English or Bulgarian.
Reuse existing SSH sessions whenever practical to reduce repeated password prompts; follow [Server access](../../docs/ai/SERVER_ACCESS.md).
Before changing project files, read the [AI documentation index](../../docs/ai/README.md) and follow its task-specific references. Treat source and tests under `server/` and `web/` as authoritative. The root [README](../../README.md) is the local operations entrypoint. Keep the API loopback-bound by default; LAN deployment is unauthenticated and must retain its specific-interface bind and host firewall allowlist.

For every test run in this repository, use `python -B scripts/run_tests.py <suite>` rather than invoking `dotnet test`, Vitest, or `unittest` directly. Choose the narrowest relevant suite and `--filter`; use `all` when full-suite verification is needed. When integration tests are relevant, check Docker availability and run `server-integration`; do not skip them just because they require Docker. If Docker is unavailable, report that blocker. When changing the runner, run the `runner` suite. See the [testing guide](../../docs/ai/TESTING.md) for suite names and examples.

## Efficient Task Workflow

- Start from the named file, symbol, failing behavior, or test. Inspect `git status --short` before editing and preserve existing user changes. Read the AI documentation index and only the task-specific references that apply.
- Before the first edit, identify the code or document that directly controls the behavior. State one falsifiable local hypothesis and the cheapest check that could disconfirm it. If one nearby dependency is unclear, read that boundary, then choose the smallest testable change.
- Make the smallest focused edit. After the first substantive edit, run the narrowest available check before further exploration or edits. For documentation-only changes, use `git diff --check` only when no more specific check exists.
- Minimize tool and approval overhead: prefer workspace file/search tools for read-only inspection, batch related safe terminal actions and final checks into one concise invocation, and keep command output bounded. Do not repeat a passing check without a change or required gate, and never bypass an approval prompt.
- If a test fails, start with the runner summary. Read only the relevant excerpt from the reported artifact, fix the cause, and rerun the same focused command. Do not repeat a passing suite without a source change or a required gate.
- Stop when the task-specific checks and required repository gates pass. Report what changed, checks run, and any blocker; do not add a retrospective to routine or single-step work.

## Plan Files

- Keep short and single-phase tasks in one task or plan. Do not create a progress file just to record commands.
- For work spanning multiple sessions or multiple independently verifiable phases, use one master plan with the goal, constraints, dependencies, phase links, and acceptance criteria.
- Add a phase file only when that phase needs its own tasks, checks, or exit criteria. Add one concise progress file only for multi-session work; track the active phase, completed checks, blockers, and next action without copying the plan.
- At session start, inspect the worktree and read the master, active phase, progress tracker if present, and relevant task documentation. Read other phase files only when a dependency requires them.
- Mention workflow friction only when it is concrete and actionable. Report approval prompts only when directly observed; do not estimate token savings or require a retrospective for every task.