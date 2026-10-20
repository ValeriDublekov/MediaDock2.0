# Database Model Review

Date: 2026-10-19
Scope: Static review of EF entities/configurations, migrations, and selected persistence/read paths. This does not include live database row counts, production query plans, or a schema change.

Disposition: Dated review, not routine task guidance. Finding 1 has since been implemented in repository source and a forward migration; finding 2 has since been implemented in source and API integration tests; findings 3-4 remain open as of 2026-10-20. No live database row counts or production query plans were inspected, and the migration has not been deployed.

| Finding | Status | Pending disposition |
| --- | --- | --- |
| 1. Overlapping `titles` and `imdb_titles` metadata | Implemented in source; deployment pending | Apply `EnforceTitleMetadataOwnership` through the staged migration process. Canonical values win conflicts; no live row audit was performed. |
| 2. Golden Globes reads expiring cache data | Implemented in source and API integration tests | None. |
| 3. Oscar film identity lacks a database consistency rule | Open | Decide whether the IMDb ID is source or canonical identity, then document/enforce that invariant. |
| 4. Search and array-filter indexes are unmeasured | Open | Collect representative production-like query plans before considering indexes. |

## Summary

The schema is not broadly incoherent: shared IMDb metadata, owner-scoped ratings/favorites, source-specific award records, and most lifecycle invariants have clear ownership and database constraints. The incomplete transition from metadata stored on `titles` to `imdb_titles` is now addressed in source by a data-preserving ownership migration and a database check. The Golden Globes catalog read-path inconsistency has also been corrected: persistent rating and poster fields now come from the linked IMDb row, and the API regression test covers both expired and missing cache entries.

## Findings

### 2. Golden Globes reads expiring cache instead of canonical metadata

Golden Globes enrichment writes verified metadata to `imdb_titles`, and each nomination's IMDb ID has a foreign key to that table. At review time, `GoldenGlobeApiService` obtained poster/rating data only from unexpired `metadata_cache` entries, so expiry or removal hid data still present in the linked canonical row. This has since been corrected: the API reads from `imdb_titles` by IMDb ID, and its integration test covers both an expired conflicting cache entry and a missing cache entry. Automatic source-type validation remains strict, while manual links retain their existing movie/series compatibility behavior.

**Risk before resolution:** user-visible metadata could disappear due to cache expiry, and rating bounds/filtering could change without the canonical record changing. This also made the TTL cache act as a second read model rather than only a resolver cache.

**Resolution:** persistent rating/poster fields now come from `imdb_titles` by IMDb ID. `metadata_cache` remains available for lookup/retry behavior and payload fields not modeled in `imdb_titles`; manual-link type-validation rules are preserved.

Relevant paths: [Golden Globes API](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeApiService.cs), [Golden Globes enrichment repository](../../server/src/MediaDock.Infrastructure/GoldenGlobes/PostgresGoldenGlobeEnrichmentRepository.cs), [metadata-cache mapping](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/MetadataCacheConfiguration.cs), [Golden Globes API tests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs).

### 3. Oscar film identity is represented twice without a database consistency rule

`OscarFilm` stores `FilmTitle`, `NormalizedTitle`, `FilmYear`, and `ImdbId`, and also has a required `TitleId`; `Title` carries corresponding title/year/IMDb identity fields. The Oscar importer validates compatibility between the film IMDb ID, linked title IMDb ID, and incoming source ID. Enrichment repeats the identity check. The database enforces both foreign keys, but does not guarantee that the two valid IMDb references identify the same title.

This duplication is partly justified: `OscarFilm` preserves source-dataset facts, which can differ from the current catalog title. It should not be removed merely to reduce column count. The IMDb identity rule, however, currently depends on application code.

**Recommendation:** document `OscarFilm.ImdbId` explicitly as either the imported source ID or the canonical linked ID. If it is source data, consider naming it `SourceImdbId` and retain the compatibility check at the import/enrichment boundary; if it is canonical, derive it from `Title` instead. Add a database-level consistency mechanism only if the chosen invariant can be expressed without rejecting legitimate null/source-snapshot cases.

Relevant paths: [OscarFilm](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarFilm.cs), [Oscar mappings](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/OscarConfigurations.cs), [Oscar importer](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs), [Oscar enrichment repository](../../server/src/MediaDock.Infrastructure/OscarAwards/PostgresOscarEnrichmentRepository.cs).

### 4. Contains-search and array filters may need different indexes at scale

Catalog search uses `ILIKE '%term%'` over title text, normalized title, and IMDb title text. Genre/country filters use array containment. The configured title index is a B-tree on `(normalized_title, year, media_type)`, which helps identity matching but not a leading-wildcard substring search; no trigram or GIN index is configured for these search paths.

**Risk:** if these tables become large, substring and array filters can require sequential scans. The workspace does not provide production-like row counts or `EXPLAIN (ANALYZE, BUFFERS)` evidence, so adding indexes now would be speculative.

**Recommendation:** measure representative catalog queries first. If they are slow, evaluate `pg_trgm` indexes for the actual `ILIKE` columns and GIN indexes for arrays; check write overhead and index size as part of that decision.

Relevant paths: [catalog query](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs), [catalog indexes](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/CatalogConfigurations.cs).

## Deliberate Denormalization To Preserve

- `Occurrence` stores feed-item/source snapshots while `Title` stores aggregate first/last observation times. These have different scopes and support history/catalog ordering.
- `OscarFilm` and `GoldenGlobeNomination` retain award-source title/year/category facts. Golden Globes are a separate award catalog and are not required to have an RSS-catalog `Title` row.
- `MetadataCacheEntry.PayloadJson` duplicates provider payload temporarily by design; it should not replace `imdb_titles` as long-lived read authority.
- `BackgroundJob` is a job-type union table with type-specific nullable columns, but its type, owner, lifecycle, and upload constraints are explicitly checked. Splitting it is not justified by this review alone.
- `PersonalRating` and `FavoriteMovie` use owner-scoped compound keys, and IMDb references consistently target `imdb_titles`; these are sound ownership choices.

## Suggested Order

1. Correct the Golden Globes read path and add the expired/missing-cache integration case.
2. Run a read-only data audit for `titles`/`imdb_titles` mismatches and IMDb-less local fallback usage.
3. Decide the field ownership and migration strategy for title metadata; preserve fallback data until its use is measured.
4. Use production-like query plans to decide whether additional search indexes are warranted.

No tests were run for this documentation-only review. No live database data or query plans were inspected.