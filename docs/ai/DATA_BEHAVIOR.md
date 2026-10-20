# Persistence Behavior

Cross-component transaction boundaries, retry/recovery, data preservation, and provider side effects. For canonical ownership and migration cautions see [Data contracts](DATA_CONTRACTS.md); for entities and constraints see [Data schema](DATA_SCHEMA.md). HTTP behavior belongs to [API contracts](API_CONTRACTS.md) and [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md). Linked services and repositories are authoritative for algorithms and persisted fields.

## RSS Ingestion

An RSS item commits its title and occurrence together, but a scan is not one transaction: run creation, parse-log writes, item upserts, and the final summary are separate operations. A failed scan can therefore leave earlier item changes committed. See [`PostgresRssIngestionRepository`](../../server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs) and [`RssIngestionService`](../../server/src/MediaDock.Application/Ingestion/RssIngestionService.cs).

## Imports And Ownership

Personal-rating import validates the complete input before opening its transaction. Owner scoping and merge-without-deletion semantics are documented in the [personal ratings API contract](API_IDENTITY_AND_PERSONAL_DATA.md); persistence is implemented by [`PersonalRatingsApiService`](../../server/src/MediaDock.Api/PersonalRatings/PersonalRatingsApiService.cs).

Oscar import upserts films and nominations transactionally while preserving populated title metadata and enrichment state; it does not create torrent occurrences or call OMDb. Golden Globes import writes award names, groups, and nominations transactionally. Reimports reuse the `(title, year, nominee_type)` group, so a new nomination inherits the group's existing link and enrichment state. Type corrections move only the affected nomination unless a verified automatic link preserves the existing type. See [`OscarDatasetImporter`](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs) and [`GoldenGlobeDatasetImporter`](../../server/src/MediaDock.Infrastructure/GoldenGlobes/GoldenGlobeDatasetImporter.cs).

## Enrichment And Retry Persistence

Oscar enrichment saves each completed result before continuing. Its run record checkpoints progress, so abrupt termination can leave a `running` record at its last committed checkpoint. See [`OscarEnrichmentService`](../../server/src/MediaDock.Application/OscarAwards/OscarEnrichmentService.cs) and [`PostgresOscarEnrichmentRunRepository`](../../server/src/MediaDock.Infrastructure/OscarAwards/PostgresOscarEnrichmentRunRepository.cs).

Manual Golden Globes linking and refresh enqueue are transactional; the group owns the link version and enrichment state. Version-checked worker updates prevent an older in-flight result from overwriting a newer manual link or clear. See [`GoldenGlobeEnrichmentService`](../../server/src/MediaDock.Application/GoldenGlobes/GoldenGlobeEnrichmentService.cs) and [`PostgresGoldenGlobeEnrichmentRepository`](../../server/src/MediaDock.Infrastructure/GoldenGlobes/PostgresGoldenGlobeEnrichmentRepository.cs).

RSS, Oscar, Golden Globes, and personal IMDb enrichment share a persisted PostgreSQL OMDb request budget by UTC day. Each HTTP attempt, including a fallback or retry, reserves atomically; cache hits do not reserve, and a reservation remains charged if the process exits before sending the request. Provider quota exhaustion blocks further reservations for that UTC day across API restarts; batch work is deferred, while local budget denial leaves unprocessed candidates eligible. See [`PostgresOmdbRequestBudget`](../../server/src/MediaDock.Infrastructure/Metadata/PostgresOmdbRequestBudget.cs) and [`ImdbTitleEnrichmentService`](../../server/src/MediaDock.Application/PersonalRatings/ImdbTitleEnrichmentService.cs).

## Cache And Coordination

The dispatcher holds a PostgreSQL session advisory lock while it runs a claimed RSS scan, Oscar/Golden Globes enrichment job, or dataset import. A competing dispatcher leaves queued work for a later poll; this lock does not serialize ordinary API writes. See [`PostgresAdvisoryScanLock`](../../server/src/MediaDock.Infrastructure/Ingestion/PostgresAdvisoryScanLock.cs) and [`BackgroundJobDispatcher`](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobDispatcher.cs).