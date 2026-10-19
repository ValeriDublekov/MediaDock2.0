# Database Model Review

Date: 2026-10-19
Scope: Static review of EF entities/configurations, migrations, and selected persistence/read paths. This does not include live database row counts, production query plans, or a schema change.

## Summary

The schema is not broadly incoherent: shared IMDb metadata, owner-scoped ratings/favorites, source-specific award records, and most lifecycle invariants have clear ownership and database constraints. The main weakness is an incomplete transition from metadata stored on `titles` to the shared `imdb_titles` table. There is also a concrete read-path inconsistency in the Golden Globes catalog, which uses expiring cache data despite having a persistent canonical IMDb row.

## Findings

### 1. `titles` and `imdb_titles` have overlapping metadata ownership

`Title` and `ImdbTitle` both define `TitleText`, `Year`, `MediaType`, `ImdbRating`, `ImdbVotes`, `Metascore`, `Genres`, `Countries`, `Director`, `Plot`, `PosterUrl`, `Runtime`, `Awards`, and `BoxOffice`. The last eleven are provider metadata; the first three may also represent local catalog identity, so they should not be merged without confirming their semantics.

The shared-IMDb migration selects the latest ID-backed `titles` row and copies its metadata into `imdb_titles`, but leaves the original columns in `titles`. The current RSS upsert ensures and updates `imdb_titles` when an IMDb ID is known. `TitleMetadataMapper` only writes the provider-detail fields to `Title` when it has no IMDb ID. Catalog reads prefer `ImdbTitle` for those details when linked and fall back to `Title` when it is not. This means the duplicate columns have a real fallback purpose, but legacy ID-backed rows can retain stale copies and the schema does not express which copy is authoritative.

**Risk:** unnecessary storage is the smaller cost; the larger one is parallel sources of truth, conditional read logic, and a future migration that could accidentally discard metadata for IMDb-less titles. `TitleText`, `Year`, and `MediaType` also appear on both entities while catalog display/filtering uses the `Title` values. They may intentionally describe the catalog record rather than the canonical IMDb record, but that distinction is not enforced by the schema.

**Recommendation:** first define field ownership. Keep `imdb_titles` authoritative for shared provider metadata. Keep catalog identity and observation fields on `titles`. Measure (a) ID-linked rows whose duplicate provider fields differ and (b) IMDb-less rows that still need local metadata. If local fallback remains necessary, consider moving it to an optional one-to-one `title_metadata` table keyed by `title_id`; otherwise clear/backfill the legacy copies and remove the unused columns in a data-preserving migration. Do not drop the fields before the two counts and fallback behavior are verified.

Relevant paths: [Title](../../server/src/MediaDock.Infrastructure/Persistence/Entities/Title.cs), [ImdbTitle](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ImdbTitle.cs), [TitleConfiguration](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/CatalogConfigurations.cs), [ImdbTitleConfiguration](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/ImdbTitleConfiguration.cs), [shared IMDb migration](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261016160138_AddSharedImdbTitles.cs), [RSS upsert](../../server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs), [catalog reads](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs).

### 2. Golden Globes reads expiring cache instead of canonical metadata

Golden Globes enrichment writes verified metadata to `imdb_titles`, and each nomination's IMDb ID has a foreign key to that table. However, `GoldenGlobeApiService` obtains poster/rating data only from unexpired `metadata_cache` entries. Once those entries expire or are removed, the API returns missing ratings/posters even though the linked `imdb_titles` row still contains the data. The existing API integration test seeds unexpired cache entries and does not cover this case.

**Risk:** user-visible metadata can disappear due to cache expiry, and rating bounds/filtering can change without the canonical record changing. This also makes the TTL cache act as a second read model rather than only a resolver cache.

**Recommendation:** read persistent rating/poster fields from `imdb_titles` by IMDb ID. Keep `metadata_cache` for lookup/retry behavior or for payload fields not modeled in `imdb_titles`; preserve the manual-link type-validation rules. Add an integration test where the linked IMDb row is populated but its cache row is expired or absent.

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