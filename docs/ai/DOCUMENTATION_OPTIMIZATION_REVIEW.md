# AI Documentation Optimization Review

Snapshot date: 2026-10-19

## Executive Summary

This is the master plan for reducing irrelevant documentation reads while preserving the repository's API, data, security, migration, and testing contracts. The assessment is divided into focused phase files so each workstream can be executed and checked without loading every recommendation.

- The 12 source documents in the review baseline contain 1,009 lines and about 167 KiB; `API_CONTRACTS.md` and `DATA_CONTRACTS.md` account for about 48% of the bytes.
- The highest-value work is to make those two cross-domain references task-routable. Splitting alone will not save much if every task still reads every new file.
- `ARCHITECTURE.md`, `TESTING.md`, and `SECURITY_AND_OPERATIONS.md` have secondary opportunities to narrow task-specific reading or remove duplicated detail.
- Keep the short orientation, access, status, and dated review documents whole unless their actual scope warrants a split.
- At the review baseline, the index pointed to a missing personal-ratings plan and described the source-size assessment as test diagnostics guidance; Phase 1 corrected those routes. Phase 2 split the API contracts into three domain references.

## Method And Limits

The size and history figures below are a snapshot of the 12 source documents reviewed; they exclude this master plan and the phase files. Git history was checked for commits touching each file and distinct commit dates. The visible history spans 2026-09-30 through 2026-10-19, only about 20 days; commit counts describe this short burst, not a durable monthly or annual cadence. A commit can touch multiple files. The repository does not provide reliable documentation-read telemetry, so likely read exposure is inferred from the task routing in [README.md](README.md), not measured.

The AI index already routes readers by task. Recommendations focus on documents that remain broad after routing, duplicated facts, and stale navigation. File size is a review trigger, not an automatic reason to split.

## Priority Scale

Priority measures expected reduction in reading and editing effort, including the cost of stale or contradictory guidance. It does not measure feature importance. 5 is the highest expected effect; 1 is little or no expected saving.

## File-By-File Review

History is shown as commits touching each file / distinct dates; size is lines / KiB. Detailed assessments and work instructions now live with their phase rather than in this routing page.

## Execution Sequence

| Phase | Focus | Dependency | Status |
| --- | --- | --- | --- |
| [Phase 1: Navigation and freshness](DOCUMENTATION_OPTIMIZATION_PHASE_1_NAVIGATION.md) | Fix stale routes, align documentation baselines, and disposition dated reviews and historical operational notes. | None | Complete |
| [Phase 2: API contracts](DOCUMENTATION_OPTIMIZATION_PHASE_2_API.md) | Turn the API contract page into a router and group observable contracts by domain. | Phase 1 | Complete |
| [Phase 3: Data contracts](DOCUMENTATION_OPTIMIZATION_PHASE_3_DATA.md) | Separate schema reference from persistence behavior while preserving ownership and migration invariants. | Phase 2, to keep API and persistence ownership distinct | Complete |
| [Phase 4: Secondary references](DOCUMENTATION_OPTIMIZATION_PHASE_4_SECONDARY.md) | Trim architecture, testing, and status guidance; retain compact conditional-read references. | Phase 3, so duplicate detail can be removed against settled API/data ownership | Planned |
| [Phase 5: Verification](DOCUMENTATION_OPTIMIZATION_PHASE_5_VERIFICATION.md) | Validate links, task routing, canonical ownership, safety constraints, and the inventory change. | Phases 1-4 | Planned |

## Execution Rules

- Read this master plan, the active phase, and only the task-specific references needed for that phase. Do not preload every phase file.
- Each phase owns its tasks, focused checks, and exit criteria. Keep the AI documentation index a task router, not a second copy of the reference material.
- Before marking an entry-page split complete, inspect each saved router from its first line through EOF. Verify its actual size and full contents fit its routing role, and that the detailed body is absent there and present only in its owning reference; patch success or seeing new text at the top is not sufficient.
- Create a progress tracker only if implementation actually spans multiple sessions. If needed, record the active phase, completed checks, blockers, and next action; do not duplicate this plan.
- Keep this work documentation-only. If source or test code must change, split that work into a separately scoped task and use the repository test runner as required.

## Overall Acceptance Criteria

- The AI index links to existing documents and accurately describes their contents.
- API and data entry pages are concise routers; detailed material is grouped by the task that changes it.
- Architecture and testing references are split only when that avoids irrelevant reads, not to meet a line-count target.
- Security, ownership, migration, observable API, error, and required test-runner rules each have a clear canonical home.
- Every new reference has a distinct reader/task and an explicit route from the index or owning entry page.
- No safety or migration constraint is lost, and repository source is never presented as proof of production state.
