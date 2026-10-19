# Source Refactoring Review

Date: 2026-10-19
Disposition: Dated source-size assessment. Recommendations remain open for separate implementation decisions; this document is not test diagnostics guidance.

## Assessment

Run on 2026-10-19: `python -B scripts/report_file_lines.py --min-lines 300`. Counts below are the script's exact results, not estimates. The largest inventory entries include the Golden Globes dataset (8,513 lines), `web/package-lock.json` (2,408), and generated EF migration designers/snapshot (up to 1,955); none is a source-refactoring candidate based on size.

Prioritized recommendations:

| Priority | File (lines) | Assessment and recommendation |
| --- | --- | --- |
| 1 | `server/tests/MediaDock.IntegrationTests/GoogleAuthenticationApiTests.cs` (1,230) | Strong independent boundaries: favorites; sign-in, identity linking, and registration; user administration. Split into focused test classes/files. Share the API factory and auth/token helpers through test support rather than duplicating them. |
| 2 | `server/tests/MediaDock.IntegrationTests/CatalogApiTests.cs` (533) | One large fact covers version/health/OpenAPI, catalog and title details, source management, settings/provider usage, parse logs, and scan runs. Split by API area, retaining isolated database state per test; do not introduce a shared mutable database just to reduce setup. |
| 3 | `server/src/MediaDock.Application/Ingestion/RssIngestionService.cs` (965) | Normal scan and failed-entry recheck orchestration coexist with shared per-entry processing, feed parsing, progress, logging/redaction, and summaries. First consider extracting recheck orchestration, including legacy-feed recovery/matching. Keep shared per-entry processing centralized; a pure feed parser is a separate, lower-priority extraction. |
| 4 | `server/src/MediaDock.Api/BackgroundJobs/BackgroundJobDispatcher.cs` (587) | Dispatcher recovery/claim/lease and terminal-state handling share a class with job-type execution handlers. Consider extracting typed handler collaborators while keeping lifecycle and job-state transitions owned by the dispatcher. |
| 5 | `server/tests/MediaDock.IntegrationTests/IngestionTests.cs` (797) | Tests cover normal scans, failed-entry rechecks, and repository upsert identity/concurrency. Organize those as focused test classes/files. Keep the large provider fixture shared only where needed; avoid copying it into each file. |
| 6 | `server/src/MediaDock.Infrastructure/Metadata/MetadataResolver.cs` (617) | General cache-backed resolution is combined with Golden Globes search, ranking, and candidate validation. This is a plausible domain boundary, but only one direct resolver test exercises the Golden Globes path. Add focused coverage for ranking, ambiguity, incomplete search, and budget outcomes before extracting it. |

Other reviewed files and disposition:

| File (lines) | Result |
| --- | --- |
| `server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs` (547) | Keep together for now: distinct job types share the same API-hosted dispatcher execution boundary and test factory. Reassess if that workflow suite continues to grow. |
| `server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs` (546) | Keep: HTTP request/budget handling, bounded response reads, provider error handling, parsing, and sanitization form one provider adapter. |
| `server/tests/MediaDock.IntegrationTests/PersonalRatingsApiTests.cs` (618) | Keep: three focused tests cover list, owner-scoped enrichment, and import under the same personal-ratings ownership boundary. |
| `server/tests/MediaDock.IntegrationTests/PersistenceTests.cs` (497) | Keep: migration, schema, and persistence invariants are related integration coverage; size alone does not justify splitting them. |
| `server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs` (472) | Keep: row preparation, identity resolution, and persistence belong to the importer operation. |
| `server/src/MediaDock.Api/BackgroundJobs/BackgroundJobApiService.cs` (470) | Keep: status/event reads and enqueue operations form the API service for the durable job resource. |
| `server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs` (462) | Keep: retry lookup, run/log persistence, item state, and catalog upsert implement one ingestion repository boundary. |
| `web/src/features/sources/BackgroundIngestionPanel.tsx` (393) | Defer: mode-specific actions sit beside shared polling, job status, events, and detail UI. If growth warrants a split, separate mode controls from the shared job view rather than duplicating polling. |

No source refactoring is included in this review. These are recommendations only; implementation requires a separate decision. Do not nominate API contracts, shared styles, migration history, datasets, lockfiles, or deployment scripts for refactoring based on size alone.