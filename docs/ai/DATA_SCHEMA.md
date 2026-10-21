# Data Schema

Compact map of the PostgreSQL persistence model in `server/`. This is an orientation page, not a field-by-field schema dump: entity files define properties, while [persistence configurations](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/) and [migrations](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/) define the database schema. For ownership and rollout cautions see [Data contracts](DATA_CONTRACTS.md); for runtime persistence behavior see [Data behavior](DATA_BEHAVIOR.md).

## Model Map

| Table | Entity | Purpose |
| --- | --- | --- |
| `imdb_titles` | [`ImdbTitle`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ImdbTitle.cs) | Canonical shared OMDb metadata and enrichment state for a normalized IMDb ID. |
| `oscar_movies` | [`OscarMovie`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarMovie.cs) | Oscar source identity and enrichment state, optionally linked to canonical IMDb metadata. |
| `oscar_nominations` | [`OscarNomination`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarNomination.cs) | Imported Oscar category, nominee, ceremony, and winner facts. |
| `golden_globe_titles` | [`GoldenGlobeTitle`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/GoldenGlobeTitle.cs) | Golden Globes title/year/nominee-type identity, IMDb link, and enrichment state. |
| `golden_globe_awards` | [`GoldenGlobeAward`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/GoldenGlobeAward.cs) | Distinct Golden Globes award names. |
| `golden_globe_nominations` | [`GoldenGlobeNomination`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/GoldenGlobeNomination.cs) | Imported award, winner, and source facts linked to a source-owned title. |
| `torrent_movies` | [`TorrentMovie`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/TorrentMovie.cs) | RSS catalog identity and observation summary; contains no provider metadata fields. |
| `torrent_releases` | [`TorrentRelease`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/TorrentRelease.cs) | Individual RSS items, unique by source and source-item key, with release metadata. |
| `sources` | [`Source`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/Source.cs) | One RSS URL per configured source profile. |
| `rss_item_states` | [`RssItemProcessingState`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/RssItemProcessingState.cs) | Fingerprint and disposition for each source/feed-item identity. |
| `scan_runs` | [`ScanRun`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ScanRun.cs) | RSS scan lifecycle and aggregate counts. |
| `parse_logs` | [`ParseLog`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ParseLog.cs) | Parse, lookup, ignore, and retry decisions for feed items. |
| `background_jobs` | [`BackgroundJob`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/BackgroundJob.cs) | Queued scans, imports, and enrichment; personal IMDb enrichment is owner-scoped. |
| `background_job_events` | [`BackgroundJobEvent`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/BackgroundJobEvent.cs) | Bounded event history for a background job. |
| `background_scheduler_state` | [`BackgroundSchedulerState`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/BackgroundSchedulerState.cs) | Scheduler checkpoint, stored as a singleton. |
| `settings` | [`AppSetting`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/AppSetting.cs) | Singleton provider and matching settings; legacy Oscar-specific limits are ignored. |
| `metadata_cache` | [`MetadataCacheEntry`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/MetadataCacheEntry.cs) | TTL resolver cache; not the canonical metadata store. |
| `oscar_enrichment_runs` | [`OscarEnrichmentRun`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OscarEnrichmentRun.cs) | Progress and outcome of an Oscar enrichment batch, separate from RSS scans. |
| `omdb_daily_usage` | [`OmdbDailyUsage`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/OmdbDailyUsage.cs) | Daily UTC OMDb usage counters and safe quota/error state. |
| `favorite_movies` | [`ImdbFavoriteMovie`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ImdbFavoriteMovie.cs) | Active user-owned watch/download markers keyed by IMDb ID. |
| `personal_ratings` | [`PersonalRating`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/PersonalRating.cs) | User-owned IMDb ratings, including titles outside the local catalog. |
| `users` | [`User`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/User.cs) | Internal account, profile, role, and lifecycle state. |
| `external_identities` | [`ExternalIdentity`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/ExternalIdentity.cs) | OIDC issuer/subject mapping to an internal user. |
| `registration_requests` | [`RegistrationRequest`](../../server/src/MediaDock.Infrastructure/Persistence/Entities/RegistrationRequest.cs) | User registration approval state; provider identity is stored separately. |

## Important Database Invariants

- Most entity tables use generated `bigint` IDs. Exceptions include `imdb_titles` keyed by normalized IMDb ID, `omdb_daily_usage` keyed by UTC date, owner-scoped composite keys for favorites and ratings, and the scheduler singleton key.
- `imdb_titles` is the sole provider-metadata owner. `oscar_movies`, `golden_globe_titles`, and `torrent_movies` may reference it; these source-owned tables keep source identity and optional IMDb links, not provider-field copies. `metadata_cache` is temporary resolver state, not canonical data.
- Oscar nominations retain imported facts and can link to `oscar_movies` without requiring a catalog or torrent record. Golden Globes titles are unique by `(title, year, nominee_type)` and preserve both movies and series; nominations retain award, winner, and import facts.
- A nonempty `torrent_movies.imdb_id` is unique. IMDb identity can merge known matches; title/year/type alone is insufficient to merge records when the IMDb ID is missing or conflicts. Each `torrent_releases` row remains distinct by `(source_id, source_item_key)`.
- `favorite_movies` is keyed by `(user_id, imdb_id)` and references `imdb_titles`. The API only adds an enriched IMDb movie; `ToDownload` is a marker and does not require a torrent release. Personal ratings remain separately owner-scoped by IMDb ID.
- `SourceOwnedMovieIdentity` creates the active source-owned tables and does not copy legacy provider fallback fields into `torrent_movies`. The final `RemoveLegacyTitleIdentity` migration drops `titles`, `occurrences`, `oscar_films`, `golden_globe_groups`, `legacy_favorite_movies`, and the old nomination link columns after reporting remaining fallback/favorite counts. Its `Down` cannot restore dropped rows; see [Data contracts](DATA_CONTRACTS.md) for rollout cautions.
- Foreign keys are restrictive except job events, which cascade with their job. OIDC issuer/subject pairs are unique; partial unique indexes also enforce one pending registration per user and prevent duplicate scheduled or active jobs.
- Database checks enforce supported type/status values, job lifecycle and ownership shape, rating bounds, user role/status combinations, and valid timestamp/count ranges. All nine `scan_runs` counters are constrained to nonnegative values. The settings row is constrained to `id = 1`; legacy Oscar-specific setting columns remain but are ignored.
- The schema has no optimistic concurrency token. For exact constraint names, column types, and index definitions, use the linked entity configurations and migrations.