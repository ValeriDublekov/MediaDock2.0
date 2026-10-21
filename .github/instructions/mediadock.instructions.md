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
- For Markdown changes, run `python -B scripts/check_markdown_links.py` to validate local links and anchors; use `python -B scripts/report_file_lines.py` when a file-line inventory is needed.
- Before the first edit, identify the code or document that directly controls the behavior. State one falsifiable local hypothesis and the cheapest check that could disconfirm it. If one nearby dependency is unclear, read that boundary, then choose the smallest testable change.
- For cross-layer or migration work, make a compact touchpoint map before editing: model/configuration, direct readers and writers, migration/snapshot, focused tests, and only the relevant docs. Use it to keep the change scoped and avoid rediscovering the same dependencies.
- For persistence identity migrations, settle canonical ownership, fallback/data-loss policy, and legacy-table transition before the first model edit. Include raw SQL and all import, ingestion, API, and web readers/writers in the touchpoint map. Scaffold schema changes with EF `migrations add`; keep custom SQL for intentional data transfer and preflight checks. Before removing/regenerating a migration, inspect its snapshot state and establish what the EF command will access or modify; do not use `migrations remove` as a routine rollback when the configured database or command effects are uncertain.
- Plan regression scenarios and verification commands before editing. Make the first patch a coherent, compileable slice, then immediately run its narrowest check; do not validate a refactor while knowingly leaving coupled call sites or fixtures incomplete. Keep required migration preflights, integration tests, and repository gates; do not trade correctness for fewer tool calls.
- Keep reads and edits local: batch independent reads, group related edits per file, and avoid rereading unchanged files. If a patch fails, inspect only the exact failed hunk and retry with its current context instead of rescanning unaffected surfaces. Add tests before the relevant suite run; after it passes, add more only when a newly identified risk justifies it, and rerun only the affected suite after a change.
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