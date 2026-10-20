# Technical Follow-Ups

These are unresolved candidates from the linked assessments, not approved delivery commitments. Make and scope each implementation decision separately; verify production data and query plans on the host where required.

## Database model

Assessment: [Database model review](DATABASE_MODEL_REVIEW.md), dated 2026-10-19.

| Finding | Tracked next step | Status |
| --- | --- | --- |
| `titles` and `imdb_titles` overlap | Define field ownership; audit mismatched linked rows and IMDb-less fallback use before deciding on a data-preserving migration. | Open decision |
| Golden Globes reads expiring cache data | Consider reading persistent rating/poster fields from `imdb_titles`; add coverage for expired or missing cache while preserving manual-link validation. | Open |
| Oscar film identity is duplicated | Decide whether the stored IMDb ID is source or canonical identity before considering naming or database consistency changes. | Open decision |
| Search and array-filter indexes are unmeasured | Collect representative query plans; consider indexes only if measurements justify their read and write costs. | Measurement pending |

## Source refactoring

Assessment: [Source refactoring review](SOURCE_REFACTORING_REVIEW_2026-10-19.md), dated 2026-10-19.

| Priority | Candidate follow-up | Status |
| --- | --- | --- |
| 1 | Split `GoogleAuthenticationApiTests` by favorites, authentication/identity, and administration while sharing test support. | Candidate |
| 2 | Split `CatalogApiTests` by API area while preserving isolated database state per test. | Candidate |
| 3 | Consider extracting failed-entry recheck orchestration from `RssIngestionService`; keep shared per-entry processing centralized. | Candidate |
| 4 | Consider typed job-handler collaborators for `BackgroundJobDispatcher`, leaving lifecycle and state transitions with the dispatcher. | Candidate |
| 5 | Organize `IngestionTests` around scan, recheck, and repository-upsert behavior without duplicating the provider fixture. | Candidate |
| 6 | Add focused Golden Globes resolver coverage before considering extraction from `MetadataResolver`. | Candidate |