# Personal IMDb Ratings and Shared Metadata Plan

## Goal

Add a paged, owner-scoped list of personal IMDb ratings with ascending and descending rating sort. Normalize shared IMDb metadata into `imdb_titles`, keyed by normalized `ImdbId`; keep `titles` as the local catalog entity. When the ratings page is opened, automatically enqueue bounded metadata enrichment through the existing background-job and shared OMDb-budget patterns.

No rating date field or date sorting is in scope.

## Decisions and Invariants

- Name the shared table `imdb_titles`, not `imdb_movies`, because the domain also includes series, documentaries, and shorts.
- `imdb_titles.imdb_id` is the normalized primary key. `titles.imdb_id` remains nullable; personal ratings retain the existing `(user_id, imdb_id)` key. Persisted IMDb identifiers on Oscar and Golden Globes records should reference the same row.
- Create an IMDb stub for every known ID even when OMDb metadata has not been fetched. Keep ratings visible in pending/not-found states and fall back to the IMDb ID when no title metadata is available.
- Backfill ID-backed metadata from `titles`. Preserve metadata on title records without an IMDb ID as title-local fallback. Never call OMDb from a database migration.
- `metadata_cache` remains a TTL resolver cache, not the canonical metadata store. Use the existing shared OMDb request budget.
- Sort only by personal rating (`asc` or `desc`), with a deterministic IMDb ID tie-breaker. Do not add a date field or date sort.
- Keep the list GET fast and owner-scoped. OMDb work must run asynchronously through the job dispatcher.

## Cross-Session Workflow

At the start of each session, read this plan, check `git status`, and inspect the current state of the target files. Do not assume the worktree is clean or that a previous session completed its exit check. Keep edits focused, run the narrow check for the touched slice, and record completion below. Do not revert unrelated user changes.

### Session 1: Schema and Migration

1. Review `docs/ai/DATA_CONTRACTS.md`, `docs/ai/ARCHITECTURE.md`, and `docs/ai/TESTING.md`; inspect current entities, configurations, and migration conventions.
2. Add an `ImdbTitle` entity and `imdb_titles` table with normalized IMDb ID PK, shared OMDb metadata, and the minimal lifecycle/retry fields needed for pending, enriched, not-found, and transient-error states.
3. Add navigation and restrictive FKs from `Title`, `PersonalRating`, `OscarFilm`, and `GoldenGlobeNomination`. Retain `ux_titles_imdb_id` and the current local-title relationships.
4. Add an additive migration and snapshot update. Before adding FKs, backfill normalized, deduplicated IDs from titles, ratings, Oscar films, and Golden Globes nominations. Copy ID-backed metadata; keep metadata for title rows without IMDb IDs unchanged.
5. Add persistence coverage for fresh schema, populated legacy data, normalized duplicate IDs, nullable/required references, and preserved no-ID title metadata.

**Dependencies:** none.

**Exit check:** `python -B scripts/run_tests.py server-integration --filter "Category=Persistence"` passes with Docker; migration and model snapshot agree.

### Session 2: Metadata Write Paths

1. Update personal-rating import, RSS upsert, Oscar import/enrichment, and Golden Globes import/manual linking to create or reuse IMDb stubs when introducing an IMDb ID.
2. Update `TitleMetadataMapper` and enrichment repositories to persist successful OMDb metadata in `imdb_titles`; keep local title identity, matching fields, and torrent observation timestamps on `titles`.
3. Preserve import counters, ownership, existing metadata, and Golden Globes cache behavior.
4. Add focused tests for stub creation, metadata preservation, ID mismatch rejection, and idempotent upserts.

**Dependencies:** Session 1. May proceed in parallel with Session 3 if edits do not overlap.

**Exit check:** focused unit and integration filters for ratings, RSS ingestion, Oscar, and Golden Globes pass.

### Session 3: Metadata Read Paths

1. Update catalog filters/projections, favorites projections, and Oscar filters/projections to read shared OMDb fields from `imdb_titles`.
2. Keep title-local fallback for records without an IMDb ID. Update Golden Globes enrichment to upsert verified metadata into the canonical row while preserving its award validation and cache behavior.
3. Add regression tests for catalog, favorites, Oscar, and Golden Globes fields and filters.

**Dependencies:** Session 1. May proceed in parallel with Session 2 if edits do not overlap.

**Exit check:** focused integration filters for catalog, favorites, Oscar, and Golden Globes pass.

### Session 4: Exact-ID Lookup and Background Enrichment

1. Extend metadata resolution to query OMDb by IMDb ID without requiring a title string; verify the returned ID and classify confirmed misses separately from transient failures.
2. Count requests against the existing shared daily budget; do not create a second budget.
3. Add a bounded `imdb_title_enrichment` job using the existing queue and dispatcher. Process only missing or due metadata, deduplicate active jobs, persist safe lifecycle/retry state, and obey batch and daily limits.
4. Add an owner-scoped enqueue endpoint that only queues work for IMDb IDs present in that user's ratings. Do not expose user rating data in global job status/events.
5. Test exact-ID requests, ID mismatch, positive/negative cache behavior, budget denial/quota stop, retry state, job deduplication, and dispatcher completion.

**Dependencies:** Sessions 1 and 2.

**Exit check:** filtered metadata resolver/budget unit tests and background-job integration tests pass.

### Session 5: Personal Ratings API

1. Add owner-scoped `GET /api/personal-ratings` with validated pagination and rating ordering (`asc`/`desc`, default `desc`), stable IMDb ID tie-break, and `PageResponse<T>`.
2. Project the personal score plus available title/poster/year/IMDb metadata and metadata status. Keep pending/not-found rows visible with IMDb ID fallback.
3. Add the enrichment enqueue route used by the page; preserve the existing import contract.
4. Test page boundaries, both sort directions, ties, empty/pending/not-found states, owner isolation, open-mode behavior, and authorization failures.

**Dependencies:** Sessions 1 and 4; uses Session 2's persistence paths.

**Exit check:** `python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~PersonalRatingsApiTests"` passes with Docker.

### Session 6: Web Client and Account View

1. Add typed list and enrichment client functions and response contracts.
2. Extend the existing Account `PersonalRatingsView` without changing its route. Show title/IMDb ID and rating; reuse shared pagination and established loading/error/empty patterns; add accessible ascending/descending rating controls and reset the page when sort changes.
3. Preserve import behavior. Automatically enqueue enrichment when the visible page has pending metadata, then refresh/poll in a bounded manner until completion or page exit. Prevent duplicate enqueue loops and keep account-switch state isolated.
4. Add client/view tests for serialization, sorting, pagination, automatic enqueue/refresh, import compatibility, and account changes.

**Dependencies:** Session 5.

**Exit check:** `python -B scripts/run_tests.py web --filter client` and a focused Personal Ratings view filter pass.

### Session 7: Documentation and Full Verification

1. Update API contracts, data contracts, architecture, implementation status, and testing docs for the new authority, FK/backfill, cache responsibilities, enrichment flow, owner scoping, and rating sort.
2. Verify migration behavior for both a fresh schema and populated existing schema. Manually check owner-only results, ascending/descending rating order, paging, metadata fallback/completion, and OMDb failure/quota handling.
3. Run focused suites for changed slices, then `python -B scripts/run_tests.py all`. Check Docker before integration tests; if unavailable, report the blocker instead of skipping the suite.

**Dependencies:** Sessions 1 through 6.

**Exit check:** documentation matches implementation, required focused/full checks pass, and unrelated worktree changes are preserved.

## Relevant Code Areas

- UI patterns and current page: `web/src/features/favorites/FavoritesView.tsx`, `web/src/features/catalog/CatalogView.tsx`, `web/src/components/Pagination.tsx`, `web/src/features/sources/ConfigurationSettingsViews.tsx`, and `web/src/App.tsx`.
- Web API and tests: `web/src/api/client.ts`, `web/src/api/types.ts`, `web/src/api/client.test.ts`, `web/src/features/sources/SourceSettingsView.test.tsx`, and `web/src/App.test.tsx`.
- Ratings API: `server/src/MediaDock.Api/PersonalRatings/PersonalRatingsEndpoints.cs`, `PersonalRatingsApiService.cs`, and `PersonalRatingsContracts.cs`.
- Persistence/writers: `server/src/MediaDock.Infrastructure/Persistence/Entities/Title.cs`, `PersonalRating.cs`, `BackgroundJob.cs`, `MediaDockDbContext.cs`, their configurations/migrations, and `server/src/MediaDock.Infrastructure/Metadata/TitleMetadataMapper.cs`.
- Metadata/jobs: `server/src/MediaDock.Application/Metadata/MetadataResolver.cs`, `server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs`, `PostgresOmdbRequestBudget.cs`, `server/src/MediaDock.Api/BackgroundJobs/BackgroundJobApiService.cs`, and `BackgroundJobDispatcher.cs`.
- Existing tests to extend: `server/tests/MediaDock.IntegrationTests/PersonalRatingsApiTests.cs`, `IdentityPersistenceTests.cs`, `BackgroundJobExecutionTests.cs`, `CatalogApiTests.cs`, `OscarApiTests.cs`, `FavoriteApiTests.cs`, and metadata resolver/OMDb budget unit tests.

## Repository Test Rule

Run all tests through `python -B scripts/run_tests.py <suite>`; do not invoke `dotnet test`, Vitest, or unittest directly.

## Progress

- [x] Session 1: schema and migration (implementation complete; focused `PersistenceTests` passes 4/4; `Category=Persistence` passes 24/24 after writer paths and FK-aware fixtures were updated)
- [x] Session 2: metadata write paths (focused ratings, RSS, Oscar, Golden Globes, and API integration filters plus Oscar and Golden Globes unit filters pass)
- [x] Session 3: metadata read paths (focused catalog, favorites, Oscar, and Golden Globes integration tests pass 8/8; Golden Globes unit tests pass 16/16)
- [ ] Session 4: exact-ID lookup and background enrichment
- [ ] Session 5: personal ratings API
- [ ] Session 6: web client and account view
- [ ] Session 7: documentation and full verification