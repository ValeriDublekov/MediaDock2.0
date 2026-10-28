# Technical Follow-Ups

These are unresolved candidates, not approved delivery commitments. Revalidate source-level items against the current code; verify production data and query plans on the host where required.

## Database model

Assessment: [Database model review](DATABASE_MODEL_REVIEW.md), dated 2026-10-19.

| Finding | Tracked next step | Status |
| --- | --- | --- |
| `titles` and `imdb_titles` overlap | Apply metadata ownership, source-owned transfer, and `RemoveLegacyTitleIdentity` through the staged migration process; inspect fallback/favorite counts and preserve a validated backup. | Source and tests complete; deployment pending |
| Golden Globes reads expiring cache data | No further action; persistent rating/poster fields now come from `imdb_titles`, with expired- and missing-cache coverage. | Implemented in source and tests |
| Oscar film identity is duplicated | Audit and resolve non-null source-ID mismatches in staging, then apply the source-owned migration chain; preflights abort on mismatches. | Source and tests complete; deployment pending |
| Search and array-filter indexes are unmeasured | Collect representative query plans; consider indexes only if measurements justify their read and write costs. | Measurement pending |

## Source refactoring

Revalidate these candidates against the current source before scheduling work.

| Priority | Candidate follow-up | Status |
| --- | --- | --- |
| 1 | Split `GoogleAuthenticationApiTests` by favorites, authentication/identity, and administration while sharing test support. | Implemented |
| 2 | Split `CatalogEndpointsApiTests` into catalog reads, RSS preview/refresh, and title deletion tests with shared seed data. | Implemented |
| 3 | Extract failed-entry recheck orchestration from `RssIngestionService`; keep shared per-entry processing centralized. | Implemented |
| 4 | Use typed job-handler collaborators for `BackgroundJobDispatcher`, leaving lifecycle and state transitions with the dispatcher. | Implemented |
| 5 | Organize `IngestionTests` around scan, recheck, and repository-upsert behavior without duplicating the provider fixture. | Implemented |
| 6 | Add focused Golden Globes resolver coverage before considering extraction from `MetadataResolver`. | Candidate |
| 7 | Review the [source complexity and file-size backlog](plans/source-complexity-refactoring.md); split only where responsibility and behavior boundaries are clear. | Candidate |