# Phase 5: Verification

Master plan: [AI Documentation Optimization Review](DOCUMENTATION_OPTIMIZATION_REVIEW.md)

## Entry Criteria

Phases 1-4 are complete, and each phase's focused checks and exit criteria have passed. If a phase is deferred, record the reason in the master plan and do not claim the overall restructure is complete.

## Verification Tasks

1. Search all Markdown files for incoming links to moved or renamed sections. Confirm every relative link resolves and every task-index description matches its destination.
2. Follow the AI index routes for API, persistence, tests, operations, and orientation. Confirm each route reaches the smallest relevant reference set and no new page is orphaned.
3. Review the API/data ownership map and the affected architecture, testing, status, and security pages. Confirm every fact has one canonical home and no observable API, access-control, secret-handling, migration, database, or test-runner constraint was lost.
4. Run `python -B scripts/report_file_lines.py` and compare the `docs/ai/` inventory with the 2026-10-19 baseline of 12 source documents, 1,009 lines, and about 167 KiB. Treat line count as a signal, not a target; the new phase files are planning overhead and are not part of the source-document baseline.
5. Run `git diff --check` and validate all edited relative Markdown links. For documentation-only changes, do not run application test suites. If source or test code changed, use `python -B scripts/run_tests.py <suite>` with the narrowest relevant suite and required integration gates.

## Exit Criteria

- All edited and incoming Markdown links resolve; the AI index and entry pages route readers accurately.
- API, persistence, architecture, testing, security, and status facts have clear owners with no meaningful duplication or lost safety constraints.
- New files are independently useful and explicitly routed; no split exists only to meet a size target.
- Inventory results are recorded as a comparison, not treated as a line-count success metric.
- Required documentation checks pass, and any source/test changes have their required runner verification.
