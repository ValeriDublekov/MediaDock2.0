# Task Workflow

Use this guide for implementation, cross-layer changes, migrations, or multi-session plans. Start with the [AI index](README.md) and the task's owning source/test; do not read the entire documentation tree.

## Local Edit Loop

1. Inspect `git status --short` and preserve existing work. Read only task-specific documentation and nearby source/tests.
2. Identify the decision owner, one falsifiable hypothesis, and the cheapest check that could disconfirm it. Resolve at most the nearby boundary needed to choose a small testable edit.
3. Plan regression scenarios and verification commands before editing. Patch a coherent, compilable slice, including coupled call sites and fixtures.
4. Immediately run the narrowest relevant check through the [test runner](TESTING.md) or [inspection helpers](DEVELOPMENT_TOOLS.md). On failure, start with the summary and read only the relevant log excerpt; fix locally and rerun the same check.
5. Widen verification only for affected behavior or a required gate. Substantial functional changes require all affected integration tests. If broader coverage is unclear, ask rather than relying on deployment to catch gaps.
6. Stop when task-specific checks and required gates pass. Report changed behavior, checks, and blockers without a routine retrospective. Suggest staging paths only when useful; never stage, commit, or push without instruction.

Batch independent reads and group related edits per file. Do not reread unchanged files or repeat passing checks without a change or required gate. If a patch fails, inspect only the failed hunk. Keep output bounded and approvals explicit; do not trade correctness for fewer tool calls.

## Cross-Layer Changes

Before editing, make a compact impact map: decision owner, direct readers/writers, boundary contracts, focused tests/fixtures. Search targeted symbols/contracts, label adjacent paths changed or intentionally unchanged, and avoid inventorying unrelated history. Implement and verify one coherent vertical slice before widening scope.

For persistence identity migrations, settle canonical ownership, fallback/data-loss policy, and legacy-table transition first. Include raw SQL plus import, ingestion, API, and web readers/writers in the map. Scaffold with EF `migrations add`; reserve custom SQL for intentional data transfer/preflight checks. Before removing or regenerating a migration, inspect its snapshot and establish which database and files the command can access or modify. Do not use `migrations remove` as routine rollback when those effects are uncertain. Keep required backup, preflight, and operator-approval gates in the [operations guide](OPERATIONS.md).

## Plans and Evidence

- Short, single-phase work stays in one task/plan; do not create a progress file just to record commands.
- Multi-session or independently verifiable phases use one master plan: goal, constraints, dependencies, phase links, and acceptance criteria. Add phase files only for separate tasks/checks/exit criteria.
- Use one concise progress tracker only for multi-session work: active phase, completed checks, blockers, next action. Do not copy the plan or raw logs into it.
- Resume from the master, active phase, existing progress tracker, and task documentation. Read another phase only for an actual dependency.
- Label each gate passed, failed, blocked, or not run. Record the command, outcome, and artifact location; a documented design or successful build is not proof of runtime behavior or production readiness.
- Keep local, disposable-test, staging, and production evidence distinct. Never copy credentials, host-private paths, or production data into plans. Report approval friction only when observed; do not invent token-saving estimates.

## Documentation Budget

Keep the always-loaded [repository instructions](../../.github/instructions/mediadock.instructions.md) small. Put task-specific detail behind the index, with one authoritative home per contract or procedure. Update links and existing owner documents rather than creating duplicate handoff files.

For new documentation, aim for focused files below 200 lines; split only at a useful topic boundary, not mechanically. When a new module needs orientation, add a short section to its existing owner document covering entrypoints, inputs/outputs, invariants, dependencies, and focused tests. Do not create context files for every directory.

Adapted from SmartReminder's indexed implementation plans and POC-readiness gates: selective reading, explicit acceptance criteria, risk-based tests, and separation of design from verified evidence. RemindCore-specific architecture, scripts, credentials, and deployment assumptions are not copied.