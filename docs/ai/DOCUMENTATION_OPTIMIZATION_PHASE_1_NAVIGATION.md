# Phase 1: Navigation and Freshness

Master plan: [AI Documentation Optimization Review](DOCUMENTATION_OPTIMIZATION_REVIEW.md)

Status: Completed 2026-10-19.

## Goal

Correct stale routes and baselines before creating more reference pages. Keep dated review artifacts out of routine task context and preserve the existing task-router role of `README.md`.

## Assessment

| File | History | Size | Disposition |
| --- | --- | --- | --- |
| [README.md](README.md) | 16 commits / 7 dates | 24 lines / 2.2 KiB | Keep concise. The personal-ratings row targets a missing plan, and the test-diagnostics row does not describe the current target. |
| [WORKFLOW_EFFICIENCY_PLAN.md](WORKFLOW_EFFICIENCY_PLAN.md) | 2 / 1 date | 59 lines / 5.2 KiB | Refresh the stale baseline: the repository instruction file now defines the ordered workflow and plan-file triggers, and `TESTING.md` documents the runner's artifact behavior. |
| [SECURITY_AND_OPERATIONS.md](SECURITY_AND_OPERATIONS.md) | 14 / 6 dates | 88 lines / 15.8 KiB | Keep stable trust, secret, SSRF, and migration-safety rules. The detailed production snapshot is historical and time-sensitive; current operations belong in the root and systemd runbooks. |
| [DATABASE_MODEL_REVIEW.md](DATABASE_MODEL_REVIEW.md) | 3 / 3 dates | 69 lines / 9.3 KiB | Preserve as a dated review until its findings have explicit dispositions; do not make it routine task guidance. |
| [SOURCE_REFACTORING_REVIEW_2026-10-19.md](SOURCE_REFACTORING_REVIEW_2026-10-19.md) | 2 / 1 date | 30 lines / 4.6 KiB | Dated source-size/refactoring assessment. The actual artifact, failure-summary, and line-inventory guidance is in `TESTING.md`. |

History and size are the 2026-10-19 review snapshot, not targets for future edits.

## Tasks

1. Inspect all incoming Markdown references to the missing personal-ratings plan. Link the index to the actual maintained plan if one exists; otherwise remove or retarget the row instead of adding an empty placeholder.
2. Resolve the test-diagnostics mismatch. The source-size assessment is archived under [SOURCE_REFACTORING_REVIEW_2026-10-19.md](SOURCE_REFACTORING_REVIEW_2026-10-19.md); remove the stale diagnostics route and restore a separate guide only if a concrete gap remains after checking `TESTING.md`.
3. Reconcile `WORKFLOW_EFFICIENCY_PLAN.md` with `.github/instructions/mediadock.instructions.md` and `TESTING.md`. Mark its completed rollout phase accurately, refresh the baseline, and retain evaluation work only while it is active. Do not duplicate mandatory instructions.
4. Decide whether the historical production snapshot in `SECURITY_AND_OPERATIONS.md` belongs in a dated handoff or should be removed after verifying that the root and systemd runbooks own any operational facts still needed. Keep current security boundaries in the security guide; do not replace history with unverified live-state claims.
5. Mark [DATABASE_MODEL_REVIEW.md](DATABASE_MODEL_REVIEW.md) and the source-size review as dated artifacts with explicit finding dispositions. Keep them out of routine task routes.

## Focused Checks

- Search all Markdown files for incoming links to the missing, renamed, or retargeted documents; confirm each destination exists and each description matches its content.
- Re-read the current instruction file, `TESTING.md`, and the canonical runbooks before updating their baselines or moving operational notes.
- Run `git diff --check` and validate every edited relative Markdown link. Do not run application test suites for documentation-only changes.

## Exit Criteria

- The index has no missing target or misleading task description, including the personal-ratings and test-diagnostics rows.
- Workflow and artifact statements match the current instruction file and test guide without duplicating their rules.
- Historical operational facts are clearly dated and separated from current operator guidance; dated reviews state whether findings remain open or are resolved.
