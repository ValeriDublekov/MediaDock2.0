# Phase 4: Secondary References

Master plan: [AI Documentation Optimization Review](DOCUMENTATION_OPTIMIZATION_REVIEW.md)

## Goal

Reduce irrelevant context in architecture, testing, and status guidance after API and data ownership is settled. Keep compact orientation and conditional-read references intact.

## Assessment

| File | History | Size | Recommendation |
| --- | --- | --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 19 commits / 9 dates | 183 lines / 20.4 KiB | Keep project boundaries, system diagram, startup, and where-to-change navigation. Remove details owned by API, data, security, and status references. Extract identity or job flows only if they remain independently useful and are routed to the relevant task. |
| [TESTING.md](TESTING.md) | 12 / 8 dates | 128 lines / 13.2 KiB | Keep the required runner, suite selection, Docker requirements, artifact behavior, and prohibition on live external-service tests. Trim inventories before considering server/web subpages. |
| [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) | 15 / 8 dates | 39 lines / 10.9 KiB | Keep one concise statement per area with authoritative links and the warning that source state does not prove production state. Remove details owned by API, data, architecture, and operations references. |
| [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md) | 4 / 3 dates | 26 lines / 2.3 KiB | Keep whole as compact orientation; trim repeated local-entry-point detail only when next touched. |
| [SERVER_ACCESS.md](SERVER_ACCESS.md) | 1 / 1 date | 51 lines / 2.2 KiB | Keep whole as a conditional-read host-access reference; do not split or broaden its index exposure. |

History and size are the 2026-10-19 review snapshot. `SECURITY_AND_OPERATIONS.md` is handled in Phase 1; retain it as a whole policy reference unless its scope grows substantially.

## Tasks

1. Condense `ARCHITECTURE.md` to project boundaries, the system diagram, startup, and change navigation. Remove duplicate contract and persistence detail after Phases 2 and 3 establish ownership.
2. Keep identity and background-job flows in architecture only if the detail is independently useful after deduplication; otherwise move it to an appropriately routed domain reference.
3. Condense `TESTING.md` around the required runner, narrow suite/filter selection, Docker-backed integration requirements, artifact behavior, and external-service prohibition. Its current artifact details should remain aligned with `scripts/run_tests.py`.
4. Split server and web testing references only if trimming still leaves frontend readers with mostly irrelevant server-specific material and the index can route around it.
5. Reduce `IMPLEMENTATION_STATUS.md` to a compact status per area plus authoritative links. Preserve the distinction between repository implementation and deployed production state.
6. Leave `PROJECT_CONTEXT.md` and `SERVER_ACCESS.md` whole unless new evidence shows their current scopes have grown enough to justify a split.

## Focused Checks

- Verify every retained architecture flow has a direct task route and is not repeated in API/data/security references.
- Compare test commands, suite rules, Docker requirements, and artifact behavior with the repository instruction file, `scripts/run_tests.py`, and the actual test projects. Do not bypass the required test runner or contact live external services.
- Verify every status claim has an authoritative source and does not imply that repository state proves production state.
- Check all relative links and run `git diff --check`. Do not run application test suites for documentation-only changes.

## Exit Criteria

- Architecture and testing are shorter only where doing so removes duplicated or irrelevant context; no new subpage exists without a distinct task route.
- Status is concise, linked to its authoritative references, and preserves the source-versus-production warning.
- Project context and server access remain compact, whole, and conditionally routed.
