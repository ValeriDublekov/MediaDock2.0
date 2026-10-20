# Data Schema

Compact map of the PostgreSQL persistence model in `server/`. This is an orientation page, not a field-by-field schema dump: entity files define properties, while [persistence configurations](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/) and [migrations](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/) define the database schema. For ownership and rollout cautions see [Data contracts](DATA_CONTRACTS.md); for runtime persistence behavior see [Data behavior](DATA_BEHAVIOR.md).

## Model Map

| Table | Entity | Purpose |
| --- | --- | --- |
| `imdb_titles` | [`ImdbTitle`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ImdbTitle.cs) | Canonical shared OMDb metadata and enrichment state for a normalized IMDb ID. |
| `titles` | [`Title`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/Title.cs) | Catalog identity and torrent-observation dates; holds provider-detail fallback only when no IMDb ID is available. |
| `sources` | [`Source`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/Source.cs) | One RSS URL per configured source profile. |
| `rss_item_states` | [`RssItemProcessingState`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/RssItemProcessingState.cs) | Fingerprint and disposition for each source/feed-item identity. |
| `occurrences` | [`Occurrence`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/Occurrence.cs) | RSS observations connecting a title to a source. |
| `scan_runs` | [`ScanRun`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ScanRun.cs) | RSS scan lifecycle and aggregate counts. |
| `parse_logs` | [`ParseLog`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ParseLog.cs) | Parse, lookup, ignore, and retry decisions for feed items. |
| `background_jobs` | [`BackgroundJob`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/BackgroundJob.cs) | Queued scans, imports, and enrichment; personal IMDb enrichment is owner-scoped. |
| `background_job_events` | [`BackgroundJobEvent`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/BackgroundJobEvent.cs) | Bounded event history for a background job. |
| `background_scheduler_state` | [`BackgroundSchedulerState`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/BackgroundSchedulerState.cs) | Scheduler checkpoint, stored as a singleton. |
| `settings` | [`AppSetting`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/AppSetting.cs) | Singleton provider and matching settings; legacy Oscar-specific limits are ignored. |
| `metadata_cache` | [`MetadataCacheEntry`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/MetadataCacheEntry.cs) | TTL resolver cache; not the canonical metadata store. |
| `oscar_films` | [`OscarFilm`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarFilm.cs) | Imported Oscar candidates linked to canonical titles, with source identity snapshots and enrichment state. |
| `oscar_nominations` | [`OscarNomination`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarNomination.cs) | Imported nominations linked to Oscar films. |
| `golden_globe_awards` | [`GoldenGlobeAward`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/GoldenGlobeAward.cs) | Unique Golden Globes award names. |
| `golden_globe_nominations` | [`GoldenGlobeNomination`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/GoldenGlobeNomination.cs) | Imported nominations linked to awards, with optional IMDb identity and enrichment state. |
| `oscar_enrichment_runs` | [`OscarEnrichmentRun`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarEnrichmentRun.cs) | Progress and outcome of an Oscar enrichment batch, separate from RSS scans. |
| `omdb_daily_usage` | [`OmdbDailyUsage`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OmdbDailyUsage.cs) | Daily UTC OMDb usage counters and safe quota/error state. |
| `favorite_movies` | [`FavoriteMovie`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/FavoriteMovie.cs) | User-owned watch/download markers for canonical titles. |
| `personal_ratings` | [`PersonalRating`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/PersonalRating.cs) | User-owned IMDb ratings, including titles outside the local catalog. |
| `users` | [`User`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/User.cs) | Internal account, profile, role, and lifecycle state. |
| `external_identities` | [`ExternalIdentity`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ExternalIdentity.cs) | OIDC issuer/subject mapping to an internal user. |
| `registration_requests` | [`RegistrationRequest`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/RegistrationRequest.cs) | User registration approval state; provider identity is stored separately. |

## Important Database Invariants

- Most entity tables use generated `bigint` IDs. Exceptions include `imdb_titles` keyed by normalized IMDb ID, `omdb_daily_usage` keyed by UTC date, owner-scoped composite keys for favorites and ratings, and the scheduler singleton key.
- `imdb_titles` is the shared metadata owner when an IMDb ID is known. `titles`, Oscar films, and Golden Globes nominations may reference it; personal ratings require it and have no `titles` foreign key. `metadata_cache` is temporary resolver state, not canonical data.
- `OscarFilm.ImdbId` is the imported source ID snapshot. A non-null value must match the linked `titles.imdb_id`; null is allowed when the source omits an ID, even if the title has one. The required `fk_oscar_films_titles_title_id` remains, while the migration-managed, initially deferred `fk_oscar_films_titles_title_id_imdb_id` enforces the non-null match using `ux_titles_id_imdb_id` and permits both linked rows to change atomically.
- `titles` provider-detail fields are allowed only when `imdb_id` is null. `ck_titles_provider_metadata_without_imdb_id` enforces this; title text, year, and media type remain catalog identity/display fields. `EnforceTitleMetadataOwnership` backfills missing shared values, preserves already populated canonical values, clears linked copies, and leaves IMDb-less fallback untouched.
- `titles.imdb_id` is unique when present, but title/year/type fallback matching is deliberately non-unique. Matching must not merge records with conflicting non-null IMDb IDs. The additive [`AddSharedImdbTitles`](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261016160138_AddSharedImdbTitles.cs) migration normalizes and consolidates existing references, preserves title-local metadata without an IMDb ID, and makes no provider requests; see [Data contracts](DATA_CONTRACTS.md) for rollout cautions.
- Favorites and ratings are keyed by owner plus title/IMDb ID. Foreign keys are restrictive except job events, which cascade with their job. OIDC issuer/subject pairs are unique; partial unique indexes also enforce one pending registration per user and prevent duplicate scheduled or active jobs.
- Database checks enforce supported type/status values, job lifecycle and ownership shape, rating bounds, user role/status combinations, and valid timestamp/count ranges. The settings row is constrained to `id = 1`; legacy Oscar-specific setting columns remain but are ignored.
- The schema has no optimistic concurrency token. For exact constraint names, column types, and index definitions, use the linked entity configurations and migrations.