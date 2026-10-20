# Database Model Review

Date: 2026-10-19
Scope: Original static review of EF entities/configurations, migrations, and selected persistence/read paths. It excludes live database row counts and production query plans; the repository changes described below have not been deployed.

Disposition: Dated review, not routine task guidance. Findings 1-3 have since been implemented in repository source and forward migrations or tests; finding 4 remains open as of 2026-10-20. No live database row counts or production query plans were inspected, and the migrations have not been deployed.

| Finding | Status | Pending disposition |
| --- | --- | --- |
| 1. Overlapping `titles` and `imdb_titles` metadata | Implemented in source; deployment pending | Apply `EnforceTitleMetadataOwnership` through the staged migration process. Canonical values win conflicts; no live row audit was performed. |
| 2. Golden Globes reads expiring cache data | Implemented in source and API integration tests | None. |
| 3. Oscar film identity lacks a database consistency rule | Implemented in source; deployment pending | Audit and resolve any non-null source-ID mismatches in staging, then apply `EnforceOscarFilmTitleIdentity`; its preflight aborts on mismatches. |
| 4. Search and array-filter indexes are unmeasured | Open | Collect representative production-like query plans before considering indexes. |

## Summary

The schema is not broadly incoherent: shared IMDb metadata, owner-scoped ratings/favorites, source-specific award records, and most lifecycle invariants have clear ownership and database constraints. The incomplete transition from metadata stored on `titles` to `imdb_titles` is now addressed in source by a data-preserving ownership migration and a database check. The Golden Globes catalog read-path inconsistency has also been corrected: persistent rating and poster fields now come from the linked IMDb row, and the API regression test covers both expired and missing cache entries. Oscar IMDb identity is now explicit source-snapshot data, with a nullable-safe database rule for non-null IDs and a migration preflight that refuses existing mismatches.

## Findings

### 2. Golden Globes reads expiring cache instead of canonical metadata

Golden Globes enrichment writes verified metadata to `imdb_titles`, and each nomination's IMDb ID has a foreign key to that table. At review time, `GoldenGlobeApiService` obtained poster/rating data only from unexpired `metadata_cache` entries, so expiry or removal hid data still present in the linked canonical row. This has since been corrected: the API reads from `imdb_titles` by IMDb ID, and its integration test covers both an expired conflicting cache entry and a missing cache entry. Automatic source-type validation remains strict, while manual links retain their existing movie/series compatibility behavior.

**Risk before resolution:** user-visible metadata could disappear due to cache expiry, and rating bounds/filtering could change without the canonical record changing. This also made the TTL cache act as a second read model rather than only a resolver cache.

**Resolution:** persistent rating/poster fields now come from `imdb_titles` by IMDb ID. `metadata_cache` remains available for lookup/retry behavior and payload fields not modeled in `imdb_titles`; manual-link type-validation rules are preserved.

Relevant paths: [Golden Globes API](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeApiService.cs), [Golden Globes enrichment repository](../../server/src/MediaDock.Infrastructure/GoldenGlobes/PostgresGoldenGlobeEnrichmentRepository.cs), [metadata-cache mapping](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/MetadataCacheConfiguration.cs), [Golden Globes API tests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs).

### 3. Oscar film identity is represented twice without a database consistency rule

`OscarFilm` stores `FilmTitle`, `NormalizedTitle`, `FilmYear`, and `ImdbId`, and also has a required `TitleId`; `Title` carries corresponding title/year/IMDb identity fields. `OscarFilm.ImdbId` is the imported source ID snapshot. It may be null when the source omits an ID even if the linked title has one; when non-null, it must match `Title.ImdbId`. The importer and enrichment code continue to validate compatibility at their boundaries.

The database retains the required `fk_oscar_films_titles_title_id` and adds `fk_oscar_films_titles_title_id_imdb_id` on `(oscar_films.title_id, oscar_films.imdb_id) -> titles(id, imdb_id)`. PostgreSQL's default `MATCH SIMPLE` permits a null source ID; the separate required title FK still guarantees that a title exists. The composite FK is initially deferred so import/enrichment transactions can update both identity values atomically. The `ux_titles_id_imdb_id` unique index supports this relationship without making nullable `titles.imdb_id` required in the EF model.

**Resolution:** `OscarFilm.ImdbId` remains source data and null-source cases remain valid. The forward migration preflights existing rows and aborts without rewriting data if a non-null source ID differs from the linked title. No live data audit was performed; inspect and resolve any mismatches in staging before deployment.

Relevant paths: [OscarFilm](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarFilm.cs), [Oscar mappings](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/OscarConfigurations.cs), [identity migration](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261020125713_EnforceOscarFilmTitleIdentity.cs), [Oscar importer](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs), [Oscar enrichment repository](../../server/src/MediaDock.Infrastructure/OscarAwards/PostgresOscarEnrichmentRepository.cs), [persistence tests](../../server/tests/MediaDock.IntegrationTests/PersistenceTests.cs).

### 4. Contains-search and array filters may need different indexes at scale

Catalog search uses `ILIKE '%term%'` over title text, normalized title, and IMDb title text. Genre/country filters use array containment. The configured title index is a B-tree on `(normalized_title, year, media_type)`, which helps identity matching but not a leading-wildcard substring search; no trigram or GIN index is configured for these search paths.

**Risk:** if these tables become large, substring and array filters can require sequential scans. The workspace does not provide production-like row counts or `EXPLAIN (ANALYZE, BUFFERS)` evidence, so adding indexes now would be speculative.

**Recommendation:** measure representative catalog queries first. If they are slow, evaluate `pg_trgm` indexes for the actual `ILIKE` columns and GIN indexes for arrays; check write overhead and index size as part of that decision.

Relevant paths: [catalog query](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs), [catalog indexes](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/CatalogConfigurations.cs).

## Deliberate Denormalization To Preserve

- `Occurrence` stores feed-item/source snapshots while `Title` stores aggregate first/last observation times. These have different scopes and support history/catalog ordering.
- `OscarFilm` and `GoldenGlobeNomination` retain award-source title/year/category facts. `OscarFilm.ImdbId` also preserves the imported source ID, subject to the non-null match with its linked title. Golden Globes are a separate award catalog and are not required to have an RSS-catalog `Title` row.
- `MetadataCacheEntry.PayloadJson` duplicates provider payload temporarily by design; it should not replace `imdb_titles` as long-lived read authority.
- `BackgroundJob` is a job-type union table with type-specific nullable columns, but its type, owner, lifecycle, and upload constraints are explicitly checked. Splitting it is not justified by this review alone.
- `PersonalRating` and `FavoriteMovie` use owner-scoped compound keys, and IMDb references consistently target `imdb_titles`; these are sound ownership choices.

## Suggested Order

1. Apply `EnforceTitleMetadataOwnership` through the staged migration process.
2. Audit and apply `EnforceOscarFilmTitleIdentity`; the migration preflight rejects non-null source-ID mismatches without rewriting rows.
3. Use production-like query plans to decide whether additional search indexes are warranted.

No live database data or production query plans were inspected; the forward migrations remain unapplied.