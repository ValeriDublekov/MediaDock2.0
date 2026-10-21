# Database Model Review

Date: 2026-10-19
Scope: Original static review of EF entities/configurations, migrations, and selected persistence/read paths. It excludes live database row counts and production query plans; the repository changes described below have not been deployed.

Disposition: Dated review of the pre-source-owned schema, not routine task guidance. Findings 1-3 have since been addressed in repository source and forward migrations or tests; finding 4 remains open as of 2026-10-20. The current EF model no longer contains the legacy `Title`/`OscarFilm`/`GoldenGlobeGroup` entities; `RemoveLegacyTitleIdentity` removes their transitional tables after source-owned transfer. No live database row counts or production query plans were inspected, and the migrations have not been deployed.

| Finding | Status | Pending disposition |
| --- | --- | --- |
| 1. Overlapping `titles` and `imdb_titles` metadata | Implemented in source; deployment pending | Apply the ownership and source-owned cleanup migrations through the staged process. Canonical values win conflicts; inspect fallback counts and take a validated backup first. |
| 2. Golden Globes reads expiring cache data | Implemented in source and API integration tests | None. |
| 3. Oscar film identity lacks a database consistency rule | Resolved in current source model; deployment pending | Audit and resolve non-null source-ID mismatches in staging, then apply the source-owned migration chain; preflights abort on mismatches. |
| 4. Search and array-filter indexes are unmeasured | Open | Collect representative production-like query plans before considering indexes. |

## Summary

The schema is not broadly incoherent: shared IMDb metadata, owner-scoped ratings/favorites, source-specific award records, and most lifecycle invariants have clear ownership and database constraints. The incomplete transition from metadata stored on `titles` to `imdb_titles` has since been addressed by an ownership migration followed by source-owned transfer and legacy cleanup; the latest migration is not deployed. The Golden Globes catalog read-path inconsistency has also been corrected: persistent rating and poster fields now come from the linked IMDb row, and the API regression test covers both expired and missing cache entries. Oscar identity now belongs to `oscar_movies`; the former linked `OscarFilm` model described below is historical to this review date.

## Findings

### 2. Golden Globes reads expiring cache instead of canonical metadata

Golden Globes enrichment writes verified metadata to `imdb_titles`, and each nomination's IMDb ID has a foreign key to that table. At review time, `GoldenGlobeApiService` obtained poster/rating data only from unexpired `metadata_cache` entries, so expiry or removal hid data still present in the linked canonical row. This has since been corrected: the API reads from `imdb_titles` by IMDb ID, and its integration test covers both an expired conflicting cache entry and a missing cache entry. Automatic source-type validation remains strict, while manual links retain their existing movie/series compatibility behavior.

**Risk before resolution:** user-visible metadata could disappear due to cache expiry, and rating bounds/filtering could change without the canonical record changing. This also made the TTL cache act as a second read model rather than only a resolver cache.

**Resolution:** persistent rating/poster fields now come from `imdb_titles` by IMDb ID. `metadata_cache` remains available for lookup/retry behavior and payload fields not modeled in `imdb_titles`; manual-link type-validation rules are preserved.

Relevant paths: [Golden Globes API](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeApiService.cs), [Golden Globes enrichment repository](../../server/src/MediaDock.Infrastructure/GoldenGlobes/PostgresGoldenGlobeEnrichmentRepository.cs), [metadata-cache mapping](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/MetadataCacheConfiguration.cs), [Golden Globes API tests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs).

### 3. Oscar film identity is represented twice without a database consistency rule

At review time, `OscarFilm` stored `FilmTitle`, `NormalizedTitle`, `FilmYear`, and `ImdbId`, and also had a required `TitleId`; `Title` carried corresponding title/year/IMDb identity fields. The current source-owned model removes that shared `Title` link and stores Oscar identity in `oscar_movies`.

The historical schema retained `fk_oscar_films_titles_title_id` and added `fk_oscar_films_titles_title_id_imdb_id` on `(oscar_films.title_id, oscar_films.imdb_id) -> titles(id, imdb_id)`. Those relationships and tables are removed by the source-owned migration chain.

**Resolution:** `OscarFilm.ImdbId` remains source data and null-source cases remain valid. The forward migration preflights existing rows and aborts without rewriting data if a non-null source ID differs from the linked title. No live data audit was performed; inspect and resolve any mismatches in staging before deployment.

Relevant paths: [OscarMovie](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarMovie.cs), [Oscar mappings](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/OscarConfigurations.cs), [source-owned migration](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261021061711_SourceOwnedMovieIdentity.cs), [legacy cleanup migration](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261021111712_RemoveLegacyTitleIdentity.cs), [Oscar importer](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs), [Oscar enrichment repository](../../server/src/MediaDock.Infrastructure/OscarAwards/PostgresOscarEnrichmentRepository.cs), [persistence tests](../../server/tests/MediaDock.IntegrationTests/PersistenceTests.cs).

### 4. Contains-search and array filters may need different indexes at scale

Catalog search uses `ILIKE '%term%'` over title text, normalized title, and IMDb title text. Genre/country filters use array containment. The configured title index is a B-tree on `(normalized_title, year, media_type)`, which helps identity matching but not a leading-wildcard substring search; no trigram or GIN index is configured for these search paths.

**Risk:** if these tables become large, substring and array filters can require sequential scans. The workspace does not provide production-like row counts or `EXPLAIN (ANALYZE, BUFFERS)` evidence, so adding indexes now would be speculative.

**Recommendation:** measure representative catalog queries first. If they are slow, evaluate `pg_trgm` indexes for the actual `ILIKE` columns and GIN indexes for arrays; check write overhead and index size as part of that decision.

Relevant paths: [catalog query](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs), [catalog indexes](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/CatalogConfigurations.cs).

## Historical Model Notes

- Before the source-owned migration, `Occurrence` stored feed-item/source snapshots while `Title` stored aggregate first/last observation times. The current model stores these scopes in `torrent_releases` and `torrent_movies`.
- Before the source-owned migration, `OscarFilm` and `GoldenGlobeGroup` shared identity through the legacy title/group tables. Current award identity belongs to `oscar_movies` and `golden_globe_titles`; nominations retain award, winner, and import facts.
- `MetadataCacheEntry.PayloadJson` duplicates provider payload temporarily by design; it should not replace `imdb_titles` as long-lived read authority.
- `BackgroundJob` is a job-type union table with type-specific nullable columns, but its type, owner, lifecycle, and upload constraints are explicitly checked. Splitting it is not justified by this review alone.
- `PersonalRating` and the active IMDb favorite model use owner-scoped compound keys, and IMDb references consistently target `imdb_titles`; these are sound ownership choices.

## Suggested Order

1. Audit staging data and apply the source-owned identity migration chain, including the destructive legacy cleanup, after a validated backup.
2. Keep the search and array-filter index question open until production-like query plans are available.

No live database data or production query plans were inspected; the forward migrations remain unapplied.