# HTTP API Contracts

This document describes the API in `server/`. Routes are registered in [Program.cs](../../server/src/MediaDock.Api/Program.cs); see [Architecture](ARCHITECTURE.md) for the cross-layer request flow.

## Routes

| Method and route | Request | Success | Other documented outcomes |
| --- | --- | --- | --- |
| `GET /health/live` | None | `200 HealthResponse` (`Status = "ok"`) | Does not query PostgreSQL. |
| `GET /health/ready` | None | `200 HealthResponse` (`Status = "ready"`) when PostgreSQL is reachable | `503 ProblemDetails` when `CanConnectAsync` is false. |
| `GET /api/version` | None | `200 VersionResponse` with the version, full source commit SHA, and UTC commit timestamp embedded in the image | Does not query PostgreSQL. |
| `GET /api/deployment` | None | `200 DeploymentStatusResponse` with systemd state, deployed SHA, failed gate/recovery markers, and up to 40 recent service output lines | `503 ProblemDetails` when the host control socket is unavailable. |
| `POST /api/deployment/run` | `{ "action": "check" }` or `{ "action": "retry_failed_gate" }` | `202 DeploymentActionResponse` after queueing the fixed systemd deploy unit | `400 ProblemDetails` for unknown actions; `409 ProblemDetails` if already running, no failed gate exists, or recovery is required; `503 ProblemDetails` if the host control socket is unavailable. Retry removes only `gate-failed`; it never clears `deploy-failed`. |
| `GET /api/catalog` | [CatalogQuery](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs) | `200 PageResponse<CatalogTitleResponse>` | `400 ValidationProblemDetails` for invalid query values. |
| `GET /api/oscars` | [OscarCatalogQuery](../../server/src/MediaDock.Api/OscarAwards/OscarContracts.cs) | `200 PageResponse<OscarFilmResponse>` | `400 ValidationProblemDetails` for invalid query values or a reversed film-year range. |
| `GET /api/golden-globes` | [GoldenGlobeCatalogQuery](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeContracts.cs) | `200 PageResponse<GoldenGlobeFilmResponse>` | `400 ValidationProblemDetails` for invalid query values or a reversed ceremony-year range. |
| `GET /api/oscars/{id:long}` | Route `id` | `200 OscarFilmResponse` with all selected-category nominations and linked title metadata | `404 ProblemDetails` when the Oscar film does not exist. |
| `GET /api/titles/{titleId:long}/oscars` | Canonical title ID | `200 OscarFilmResponse[]` for all linked Oscar records and nominations, or an empty array | `404 ProblemDetails` when the title does not exist. |
| `GET /api/favorites` | `status=all|to_watch|to_download`, `page`, `pageSize` | `200 PageResponse<FavoriteMovieResponse>` | `400 ValidationProblemDetails` for invalid query values. |
| `POST /api/favorites` | `titleId`, `from=oscar|catalog` | `200 FavoriteMovieResponse` | `400 ValidationProblemDetails`; `404 ProblemDetails` if the movie or requested source is absent. |
| `PATCH /api/favorites/{titleId:long}` | Optional `toWatch`, `toDownload` booleans (at least one) | `200 FavoriteMovieResponse` | `400 ValidationProblemDetails` for empty input or enabling download without an occurrence; `404 ProblemDetails` if not a favorite. |
| `DELETE /api/favorites/{titleId:long}` | Canonical title ID | `204`, also for absent favorites | None declared. |
| `GET /api/titles/{id:long}` | Route `id` | `200 TitleDetailsResponse` | `404 ProblemDetails` when the title does not exist. |
| `GET /api/titles/{id:long}/occurrences` | Route `id`, [OccurrencesQuery](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs) | `200 PageResponse<OccurrenceResponse>` | `400 ValidationProblemDetails`; `404 ProblemDetails` when the title does not exist. |
| `GET /api/sources` | None | `200 IReadOnlyList<SourceProfileResponse>` in fixed `movie`, `series_complete`, `series_ongoing` order, each with its configured URLs | None declared. |
| `POST /api/sources/{profileId}/urls` | Route profile ID, [SourceUrlRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `201 SourceUrlResponse` | `400 ValidationProblemDetails` for an unknown profile or invalid URL; `409 ProblemDetails` for a URL already assigned/configured. |
| `PUT /api/sources/{profileId}/urls/{id:long}` | Route profile ID and URL ID, [SourceUrlRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 SourceUrlResponse` | `400 ValidationProblemDetails` for an unknown profile or invalid URL; `404 ProblemDetails` when the URL is not active in that profile; `409 ProblemDetails` for a duplicate URL. |
| `DELETE /api/sources/{profileId}/urls/{id:long}` | Route profile ID and URL ID | `204` | `400 ValidationProblemDetails` for an unknown profile; `404 ProblemDetails` when the URL is not active in that profile. The row is disabled to retain occurrence/history foreign keys. |
| `GET /api/settings` | None | `200 SettingsResponse` | None declared. |
| `PUT /api/settings` | [UpdateSettingsRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 SettingsResponse` | `400 ValidationProblemDetails`. |
| `GET /api/settings/providers/omdb` | None | `200 ProviderSettingsResponse`; returns `OmdbApiKeyConfigured` and limits, never the key | None declared. |
| `GET /api/settings/providers/omdb/usage` | None | `200 OmdbDailyUsageResponse[]` for usage rows in the last 30 UTC days, newest first; returns aggregate counts and safe error codes only | None declared. |
| `PUT /api/settings/providers/omdb` | [UpdateProviderSettingsRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 ProviderSettingsResponse` | `400 ValidationProblemDetails`; unauthenticated LAN-trusted write. |
| `POST /api/personal-ratings/import` | Multipart `File` (JSON array of `{ id, rating }`) | `200 PersonalRatingsImportResponse` with added, updated, unchanged, and total counts, plus per-ID errors for rows with missing ratings; valid rows are merged | `400 ValidationProblemDetails` for missing/oversized/non-JSON files or other invalid entries; unauthenticated LAN-trusted write. |
| `GET /api/background-jobs/active` | None | `200 BackgroundJobResponse` or `204` when no job is queued/running | None declared. |
| `POST /api/background-jobs/scans` | Empty body | `202 BackgroundJobAcceptedResponse` with job ID and status URL | `409 ProblemDetails` with `activeJobId` and `activeJobStatusUrl` if an RSS scan is already queued/running. |
| `POST /api/background-jobs/oscar-enrichment` | Empty body | `202 BackgroundJobAcceptedResponse` with job ID and status URL | `409 ProblemDetails` if an Oscar enrichment job is already queued/running. |
| `POST /api/background-jobs/oscar-import` | Multipart `File` (CSV/TSV) and optional `YearAfter` | `202 BackgroundJobAcceptedResponse` | `400 ValidationProblemDetails` for empty/oversized file, unsupported filename/content type, or invalid year. Client paths are not accepted. |
| `POST /api/background-jobs/golden-globe-enrichment` | Empty body | `202 BackgroundJobAcceptedResponse` | `409 ProblemDetails` if a Golden Globes enrichment job is already queued or running. |
| `POST /api/background-jobs/golden-globe-import` | Multipart `File` (CSV/TSV) and optional `YearAfter` | `202 BackgroundJobAcceptedResponse` | `400 ValidationProblemDetails` for empty/oversized file, unsupported filename extension, or invalid year. Client paths are not accepted. |
| `GET /api/background-jobs/{id:long}` | Job ID | `200 BackgroundJobResponse` with lifecycle, progress, safe summary/error code, and optional `scanRunId` | `404 ProblemDetails` for unknown ID. |
| `GET /api/background-jobs/{id:long}/events` | `afterId` (default 0), `pageSize` (default 50, max 100) | `200 BackgroundJobEventsResponse` ordered by event ID | `400 ValidationProblemDetails`; `404 ProblemDetails` for unknown job. |
| `GET /api/parse-logs` | [ParseLogQuery](../../server/src/MediaDock.Api/Operations/OperationsContracts.cs) | `200 PageResponse<ParseLogResponse>` | `400 ValidationProblemDetails` for invalid query values. |
| `GET /api/scan-runs` | [ScanRunQuery](../../server/src/MediaDock.Api/Operations/OperationsContracts.cs) | `200 PageResponse<ScanRunResponse>` | `400 ValidationProblemDetails` for invalid query values. This lists history; it does not start a scan. |
| `GET /openapi/v1.json` | None | OpenAPI document in Development only | `MapOpenApi` is registered only in Development. |

The endpoint mappings and response metadata are in [CatalogEndpoints.cs](../../server/src/MediaDock.Api/Catalog/CatalogEndpoints.cs), [OscarEndpoints.cs](../../server/src/MediaDock.Api/OscarAwards/OscarEndpoints.cs), [SourceSettingsEndpoints.cs](../../server/src/MediaDock.Api/Sources/SourceSettingsEndpoints.cs), [OperationalHistoryEndpoints.cs](../../server/src/MediaDock.Api/Operations/OperationalHistoryEndpoints.cs), [DeploymentControlEndpoints.cs](../../server/src/MediaDock.Api/Operations/DeploymentControlEndpoints.cs), [BackgroundJobEndpoints.cs](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobEndpoints.cs), and [HealthEndpoints.cs](../../server/src/MediaDock.Api/Health/HealthEndpoints.cs). Background-job API coverage is in [BackgroundJobApiTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobApiTests.cs) and [BackgroundJobExecutionTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs); deployment-control API coverage is in [DeploymentControlApiTests.cs](../../server/tests/MediaDock.IntegrationTests/DeploymentControlApiTests.cs).

The Golden Globes catalog route and response metadata are registered in [GoldenGlobeEndpoints.cs](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeEndpoints.cs) and defined in [GoldenGlobeContracts.cs](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeContracts.cs).

Deployment status and start routes proxy a fixed request set to the host-side Unix-socket service. They do not accept commands, paths, or unit names from clients. The routes remain unauthenticated and must only be reachable inside the trusted LAN boundary.

Background job types are `rss_scan` (manual or scheduled), `oscar_enrichment`, `oscar_import`, `golden_globe_enrichment`, and `golden_globe_import` (all manual). RSS scans do not run award-catalog imports or enrichment as follow-up operations.

## Queries And Validation

All paged query DTOs accept optional `Page` in `1..1,000,000` and `PageSize` in `1..100`; omitted values are `1` and `25`. Invalid annotated values produce a 400 validation problem. Pages use offset pagination; a page past the end returns an empty `Items` list rather than being clamped.

| Query DTO | Additional fields and behavior |
| --- | --- |
| `CatalogQuery` | `Search` (max 200 characters) is trimmed and matched with PostgreSQL `ILIKE` against title and normalized title. `MediaType` is `movie`, `series`, `documentary`, or `short`; `SourceType` is `movie` or `series`; `FeedTypes` is a comma-separated list of `movie`, `series_complete`, and `series_ongoing`, matching titles with at least one occurrence from a listed feed; `ContentKind` is `standard`, `documentary`, or `short`. `YearFrom` and `YearTo` each range from 1800 through 2200 and are inclusive; `YearFrom` greater than `YearTo` is a validation error. `Genre` and `Country` are trimmed exact array-membership filters (max 100 characters each). `SourceId` must be at least 1 and matches titles having an occurrence from that source. The catalog list and `TotalCount` always exclude titles without any occurrence; Oscar-only films are listed by the Oscar API. Supplied filters are combined. |
| `OscarCatalogQuery` | `Search` (max 200 characters) is trimmed and matched with `ILIKE` against the Oscar CSV title, normalized title, and linked title metadata. `YearFrom` and `YearTo` range from 1800 through 2200, are inclusive, and filter the Oscar film year; a reversed range is a validation error. `Category` (max 100 characters) matches a canonical category case-insensitively. `Result` is `winner` or `nominee`; each matches films with at least one winning or non-winning nomination in the selected category. `EnrichmentStatus` is `pending`, `enriched`, `not_found`, or `temporary_error`. Supplied filters are combined. |
| `GoldenGlobeCatalogQuery` | `Search` (max 200 characters) is trimmed and matched with `ILIKE` against nomination title; `YearFrom` and `YearTo` range from 1800 through 2200 and filter ceremony year inclusively; a reversed range is a validation error. `Award` (max 100 characters) is trimmed and matched with `ILIKE`. `Result` is `winner` or `nominee` and filters nominations before grouping them by title and year. `EnrichmentStatus` is `pending`, `enriched`, `problem`, `not_found`, or `temporary_error` and filters grouped titles. Supplied filters are combined; pagination applies to grouped films. |
| `OccurrencesQuery` | Only the common `Page` and `PageSize` fields. |
| `ParseLogQuery` | `SourceId` and `ScanRunId` must be at least 1; `ParsedSuccessfully` and `Ignored` are optional booleans; `RetryState` is `retryable`, `terminal`, or `resolved`; `Search` (max 200 characters) is trimmed and matched with `ILIKE` against raw title, feed name, or parsed title. Supplied filters are combined. |
| `ScanRunQuery` | `Status` is `running`, `succeeded`, `partial`, or `failed`; `Trigger` is `schedule`, `manual`, or `local`. Supplied filters are combined. |

Catalog titles with occurrences sort by `LastSeenAt` descending, then `Id` descending; Oscar films sort by film year descending, normalized title ascending, then `Id` ascending; occurrences use the catalog ordering. Oscar-only titles can still be opened through `GET /api/titles/{id}` when following an Oscar catalog link. Parse logs sort by `ProcessedAt` descending, then `Id` descending; scan runs sort by `StartedAt` descending, then `Id` descending. `PageResponse<T>` contains `Items`, `Page`, `PageSize`, `TotalCount`, and `TotalPages`; `TotalPages` is zero for an empty result and otherwise the ceiling of `TotalCount / PageSize`.

## Request DTOs

| DTO | Fields and validation |
| --- | --- |
| `SourceUrlRequest` | `Url` is required, `[Url]`, max 2048. The service trims outer whitespace, then accepts only whitespace-free absolute HTTPS URLs without user-info on `feed.rutracker.cc`. The route profile ID is authoritative; the request has no name, type, key, enable flag, or parser option. |
| `UpdateSettingsRequest` | `ExcludedGenres` and `ExcludedCountries` are required arrays of at most 100 values. Each value must be nonblank and at most 100 characters; values are trimmed, de-duplicated case-insensitively, and sorted case-insensitively. `MinMovieRating` and `MinSeriesRating` are each `0..10`; `MinImdbVotes` is `0..1,000,000,000`. |
| `UpdateProviderSettingsRequest` | `OmdbApiKey` is optional and max 512 characters; blank/omitted preserves the saved key. Set `ClearOmdbApiKey` to remove it, and do not provide a new key in the same request. The shared daily request limit is a non-negative integer; a configured key requires a positive value. |
| `PersonalRatingsImportForm` | Required `.json` file, max 5 MiB and 50,000 entries. Each entry has an IMDb title `id` and integer `rating` (1..10). Rows with a valid ID but a missing/null rating are skipped and listed in the success response; other invalid entries reject the file before database changes. |
| `GoldenGlobeImportForm` | Required non-empty `.csv` or `.tsv` file within the configured background-job upload limit; `YearAfter` is optional and defaults to 1980, with accepted values `0..9998`. The dataset is parsed asynchronously after queueing. |

The profile ID is a fixed system value and the only selector for RSS behavior. `movie` uses movie parsing, OMDb `type=movie`, and the parsed release year for lookup/matching. `series_complete` and `series_ongoing` both use OMDb `type=series` without `y`; they have separate season-pack and episode-marker parsing rules. A parsed season/episode year is retained in parse history but is not used as the show's premiere year for lookup or matching. Legacy `series` source records migrate to `series_ongoing`.

## Response DTOs

Nullable response fields are marked `?`; collection fields are returned as lists/arrays.

| DTO | Fields |
| --- | --- |
| `VersionResponse` | `Version` (`YYYY.MM.DD+<7-character-SHA>` for automated deployments), `CommitSha`, `CommitDateUtc?` |
| `DeploymentStatusResponse` | `IsRunning`, `ActiveState`, `SubState`, `Result`, `ExitCode`, `StartedAt`, `FinishedAt`, `DeployedSha?`, `GateFailedSha?`, `RecoveryRequired`, `FailureTargetSha?`, `RecentOutput` (up to 40 lines) |
| `DeploymentActionRequest` | `Action` (`check` or `retry_failed_gate`) |
| `DeploymentActionResponse` | `Message` |
| `HealthResponse` | `Status` |
| `PageResponse<T>` | `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages` |
| `CatalogTitleResponse` | `Id`, `Title`, `Year?`, `MediaType`, `SourceType?`, `ContentKind?`, `ImdbId?`, `ImdbRating?`, `PosterUrl?`, `Genres`, `Countries`, `LastSeenAt?`, `OccurrenceCount` |
| `OscarFilmResponse` | `Id`, `TitleId`, `Title` (CSV title), `MetadataTitle`, `MetadataYear?`, `FilmYear`, `ImdbId?`, `EnrichmentStatus`, `EnrichmentAttemptCount`, `LastEnrichmentAttemptAt?`, `NextEnrichmentAttemptAt?`, `LastEnrichmentError?`, `MediaType`, `ImdbRating?`, `ImdbVotes?`, `Metascore?`, `Genres`, `Countries`, `Director?`, `Plot?`, `PosterUrl?`, `Runtime?`, `Awards?`, `BoxOffice?`, `Nominations` |
| `OscarNominationResponse` | `Id`, `Ceremony`, `Class`, `CanonicalCategory`, `Category`, `Name`, `Nominees`, `NomineeIds`, `Detail`, `IsWinner` |
| `GoldenGlobeFilmResponse` | `Id` (`ceremonyYear:title`), `Title`, `Year`, `ImdbId?`, `PosterUrl?` (from unexpired OMDb metadata cache), `EnrichmentStatus?`, `EnrichmentError?`, `Nominations` |
| `GoldenGlobeNominationResponse` | `Id`, `Year`, `Award`, `IsWinner` |
| `TitleDetailsResponse` | `Id`, `Title`, `Year?`, `MediaType`, `SourceType?`, `ContentKind?`, `BroadcastRangeStartYear?`, `BroadcastRangeEndYear?`, `BroadcastRangeRaw?`, `ImdbId?`, `ImdbRating?`, `ImdbVotes?`, `Metascore?`, `Genres`, `Countries`, `Director?`, `Plot?`, `PosterUrl?`, `Runtime?`, `Awards?`, `BoxOffice?`, `FirstSeenAt?`, `LastSeenAt?`, `UpdatedAt`, `OccurrenceCount` |
| `OccurrenceResponse` | `Id`, `TitleId`, `SourceId`, `SourceName`, `SourceItemKey`, `FeedEntryId?`, `TorrentUrl`, `RawTitle`, `SourceFeedName`, `FeedType?`, `SourcePublishedAt?`, `ObservedAt?`, `Quality?`, `RipType?`, `FirstSeenAt`, `LastSeenAt` |
| `SourceProfileResponse` | `Id` (fixed profile ID), `Name` (fixed display label), `Urls` |
| `SourceUrlResponse` | `Id`, `Url` |
| `SettingsResponse` | `ExcludedGenres`, `ExcludedCountries`, `MinMovieRating`, `MinSeriesRating`, `MinImdbVotes`, `UpdatedAt?` |
| `ProviderSettingsResponse` | `OmdbApiKeyConfigured`, `OmdbDailyRequestLimit`, `UpdatedAt?`; does not include the key. |
| `PersonalRatingsImportResponse` | `RatingsInFile`, `Added`, `Updated`, `Unchanged`, `TotalRatings`, `ImportedAt`, `Errors` (`Id`, `Message`) |
| `OmdbDailyUsageResponse` | `UtcDate`, `TotalRequests`, `RssRequests`, `OscarRequests`, `DailyRequestLimitReached`, `ProviderQuotaExceeded`, `LastErrorCode?`; counts are conservative reservations and can include an attempt interrupted before sending. |
| `ParseLogResponse` | `Id`, `SourceId?`, `SourceName?`, `SourceItemKey?`, `RawTitle`, `FeedName`, `ParsedSuccessfully`, `ParsedTitle?`, `ParsedYear?`, `OmdbStatus`, `Ignored`, `IgnoreReason?`, `ErrorMessage?`, `Decision?`, `ProcessedAt`, `RetryState`, `AttemptCount`, `LastAttemptAt?`, `FeedType?`, `SourcePublishedAt?`, `ObservedAt?`, `EventKind?` |
| `ScanRunResponse` | `Id`, `StartedAt`, `FinishedAt?`, `Status`, `Trigger`, `FeedsProcessed`, `EntriesSeen`, `KnownEntriesSkipped`, `TitlesCreated`, `OccurrencesCreated`, `CacheHits`, `OmdbRequests` (actual HTTP attempts), `IgnoredEntries`, `ErrorCount`, `ErrorSummary` |
| `BackgroundJobResponse` | `Id`, `JobType`, `Trigger`, `Status`, `EnqueuedAt`, `StartedAt?`, `FinishedAt?`, `CurrentStage?`, `CurrentSource?`, `ProgressUpdatedAt?`, `ErrorCode?`, `ResultSummary?`, `ScanRunId?`, `InputFileName?`; never contains CSV bytes, credentials, stack traces, or provider URLs. |
| `BackgroundJobAcceptedResponse` | `Id`, `Status`, `StatusUrl` |
| `BackgroundJobEventResponse` | `Id`, `OccurredAt`, `Level`, `EventCode`, `Message`, `Data?`; event fields are safe, bounded projections. |
| `BackgroundJobEventsResponse` | `Items`, `NextAfterId` |
| `FavoriteMovieResponse` | `TitleId`, `Title`, `Year?`, `MediaType`, `ImdbRating?`, `PosterUrl?`, `ToWatch`, `ToDownload`, `AddedFromOscar`, `AddedFromCatalog`, `CreatedAt`, `UpdatedAt`, `OscarFilmCount`, `NominationCount`, `WinCount`, `OccurrenceCount`, `LastSeenAt?` |

Favorites are one shared local list with no user identity. Only movies with the selected source record can be added. Repeated additions from the same source preserve manually disabled markers; a new source sets only its own default marker. Status filtering happens before pagination; results order by `UpdatedAt DESC, TitleId DESC`. The response summarizes occurrences rather than embedding torrent URLs; fetch details through the title endpoints. The API is unauthenticated and must remain within the documented loopback/LAN firewall boundary.

When no settings row exists, `GET /api/settings` returns empty exclusion arrays, zero thresholds, and `UpdatedAt = null`. Writes create or update the singleton row with `id = 1`; concurrent first initialization resolves to that row.
When no settings row exists, `GET /api/settings/providers/omdb` returns `OmdbApiKeyConfigured = false` and zero limits.

## Error And Access Semantics

[ApiExceptionHandler.cs](../../server/src/MediaDock.Api/Middleware/ApiExceptionHandler.cs) maps `ApiValidationException` to 400 `ValidationProblemDetails`, `ApiNotFoundException` to 404 `ProblemDetails`, and `ApiConflictException` to 409 `ProblemDetails`, all with `application/problem+json`. Unrecognized exceptions are not mapped by this handler; this document does not promise their response shape. Readiness failure is a separate 503 `ProblemDetails` response.

`Program.cs` does not register authentication or authorization middleware. Source, provider-settings, and personal-rating writes and background-job producer endpoints are unauthenticated and LAN-trusted; do not infer an authentication contract from these routes. The provider settings response never returns the saved API key. Uploaded rating JSON is parsed in memory and is not stored as a file. Job endpoints expose no upload bytes, raw exception text, stack trace, provider request URL, or OMDb key.