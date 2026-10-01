# HTTP API Contracts

This document describes the API in `server/`. Routes are registered in [Program.cs](../../server/src/MediaDock.Api/Program.cs); see [Architecture](ARCHITECTURE.md) for the cross-layer request flow.

## Routes

| Method and route | Request | Success | Other documented outcomes |
| --- | --- | --- | --- |
| `GET /health/live` | None | `200 HealthResponse` (`Status = "ok"`) | Does not query PostgreSQL. |
| `GET /health/ready` | None | `200 HealthResponse` (`Status = "ready"`) when PostgreSQL is reachable | `503 ProblemDetails` when `CanConnectAsync` is false. |
| `GET /api/version` | None | `200 VersionResponse` with the version, full source commit SHA, and UTC commit timestamp embedded in the image | Does not query PostgreSQL. |
| `GET /api/catalog` | [CatalogQuery](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs) | `200 PageResponse<CatalogTitleResponse>` | `400 ValidationProblemDetails` for invalid query values. |
| `GET /api/oscars` | [OscarCatalogQuery](../../server/src/MediaDock.Api/OscarAwards/OscarContracts.cs) | `200 PageResponse<OscarFilmResponse>` | `400 ValidationProblemDetails` for invalid query values or a reversed film-year range. |
| `GET /api/oscars/{id:long}` | Route `id` | `200 OscarFilmResponse` with all selected-category nominations and linked title metadata | `404 ProblemDetails` when the Oscar film does not exist. |
| `GET /api/titles/{titleId:long}/oscars` | Canonical title ID | `200 OscarFilmResponse[]` for all linked Oscar records and nominations, or an empty array | `404 ProblemDetails` when the title does not exist. |
| `GET /api/favorites` | `status=all|to_watch|to_download`, `page`, `pageSize` | `200 PageResponse<FavoriteMovieResponse>` | `400 ValidationProblemDetails` for invalid query values. |
| `POST /api/favorites` | `titleId`, `from=oscar|catalog` | `200 FavoriteMovieResponse` | `400 ValidationProblemDetails`; `404 ProblemDetails` if the movie or requested source is absent. |
| `PATCH /api/favorites/{titleId:long}` | Optional `toWatch`, `toDownload` booleans (at least one) | `200 FavoriteMovieResponse` | `400 ValidationProblemDetails` for empty input or enabling download without an occurrence; `404 ProblemDetails` if not a favorite. |
| `DELETE /api/favorites/{titleId:long}` | Canonical title ID | `204`, also for absent favorites | None declared. |
| `GET /api/titles/{id:long}` | Route `id` | `200 TitleDetailsResponse` | `404 ProblemDetails` when the title does not exist. |
| `GET /api/titles/{id:long}/occurrences` | Route `id`, [OccurrencesQuery](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs) | `200 PageResponse<OccurrenceResponse>` | `400 ValidationProblemDetails`; `404 ProblemDetails` when the title does not exist. |
| `GET /api/sources` | None | `200 IReadOnlyList<SourceResponse>`, ordered by `Name` then `Id` | None declared. |
| `GET /api/sources/{id:long}` | Route `id` | `200 SourceResponse` | `404 ProblemDetails` when the source does not exist. |
| `POST /api/sources` | [CreateSourceRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `201 SourceResponse`, with `Location: /api/sources/{id}` | `400 ValidationProblemDetails`; `409 ProblemDetails` for an existing stable key. |
| `PUT /api/sources/{id:long}` | Route `id`, [UpdateSourceRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 SourceResponse` | `400 ValidationProblemDetails`; `404 ProblemDetails`; `409 ProblemDetails` for an existing stable key. |
| `GET /api/settings` | None | `200 SettingsResponse` | None declared. |
| `PUT /api/settings` | [UpdateSettingsRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 SettingsResponse` | `400 ValidationProblemDetails`. |
| `GET /api/settings/providers/omdb` | None | `200 ProviderSettingsResponse`; returns `OmdbApiKeyConfigured` and limits, never the key | None declared. |
| `PUT /api/settings/providers/omdb` | [UpdateProviderSettingsRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 ProviderSettingsResponse` | `400 ValidationProblemDetails`; unauthenticated LAN-trusted write. |
| `GET /api/background-jobs/active` | None | `200 BackgroundJobResponse` or `204` when no job is queued/running | None declared. |
| `POST /api/background-jobs/scans` | Empty body | `202 BackgroundJobAcceptedResponse` with job ID and status URL | `409 ProblemDetails` with `activeJobId` and `activeJobStatusUrl` if an RSS scan is already queued/running. |
| `POST /api/background-jobs/oscar-import` | Multipart `File` (CSV/TSV) and optional `YearAfter` | `202 BackgroundJobAcceptedResponse` | `400 ValidationProblemDetails` for empty/oversized file, unsupported filename/content type, or invalid year. Client paths are not accepted. |
| `GET /api/background-jobs/{id:long}` | Job ID | `200 BackgroundJobResponse` with lifecycle, progress, safe summary/error code, and optional `scanRunId` | `404 ProblemDetails` for unknown ID. |
| `GET /api/background-jobs/{id:long}/events` | `afterId` (default 0), `pageSize` (default 50, max 100) | `200 BackgroundJobEventsResponse` ordered by event ID | `400 ValidationProblemDetails`; `404 ProblemDetails` for unknown job. |
| `GET /api/parse-logs` | [ParseLogQuery](../../server/src/MediaDock.Api/Operations/OperationsContracts.cs) | `200 PageResponse<ParseLogResponse>` | `400 ValidationProblemDetails` for invalid query values. |
| `GET /api/scan-runs` | [ScanRunQuery](../../server/src/MediaDock.Api/Operations/OperationsContracts.cs) | `200 PageResponse<ScanRunResponse>` | `400 ValidationProblemDetails` for invalid query values. This lists history; it does not start a scan. |
| `GET /openapi/v1.json` | None | OpenAPI document in Development only | `MapOpenApi` is registered only in Development. |

The endpoint mappings and response metadata are in [CatalogEndpoints.cs](../../server/src/MediaDock.Api/Catalog/CatalogEndpoints.cs), [OscarEndpoints.cs](../../server/src/MediaDock.Api/OscarAwards/OscarEndpoints.cs), [SourceSettingsEndpoints.cs](../../server/src/MediaDock.Api/Sources/SourceSettingsEndpoints.cs), [OperationalHistoryEndpoints.cs](../../server/src/MediaDock.Api/Operations/OperationalHistoryEndpoints.cs), [BackgroundJobEndpoints.cs](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobEndpoints.cs), and [HealthEndpoints.cs](../../server/src/MediaDock.Api/Health/HealthEndpoints.cs). Background-job API coverage is in [BackgroundJobApiTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobApiTests.cs) and [BackgroundJobExecutionTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs).

## Queries And Validation

All paged query DTOs accept optional `Page` in `1..1,000,000` and `PageSize` in `1..100`; omitted values are `1` and `25`. Invalid annotated values produce a 400 validation problem. Pages use offset pagination; a page past the end returns an empty `Items` list rather than being clamped.

| Query DTO | Additional fields and behavior |
| --- | --- |
| `CatalogQuery` | `Search` (max 200 characters) is trimmed and matched with PostgreSQL `ILIKE` against title and normalized title. `MediaType` is `movie`, `series`, `documentary`, or `short`; `SourceType` is `movie` or `series`; `ContentKind` is `standard`, `documentary`, or `short`. `YearFrom` and `YearTo` each range from 1800 through 2200 and are inclusive; `YearFrom` greater than `YearTo` is a validation error. `Genre` and `Country` are trimmed exact array-membership filters (max 100 characters each). `SourceId` must be at least 1 and matches titles having an occurrence from that source. The catalog list and `TotalCount` always exclude titles without any occurrence; Oscar-only films are listed by the Oscar API. Supplied filters are combined. |
| `OscarCatalogQuery` | `Search` (max 200 characters) is trimmed and matched with `ILIKE` against the Oscar CSV title, normalized title, and linked title metadata. `YearFrom` and `YearTo` range from 1800 through 2200, are inclusive, and filter the Oscar film year; a reversed range is a validation error. `Category` (max 100 characters) matches a canonical category case-insensitively. `Result` is `winner` or `nominee`; each matches films with at least one winning or non-winning nomination in the selected category. `EnrichmentStatus` is `pending`, `enriched`, `not_found`, or `temporary_error`. Supplied filters are combined. |
| `OccurrencesQuery` | Only the common `Page` and `PageSize` fields. |
| `ParseLogQuery` | `SourceId` and `ScanRunId` must be at least 1; `ParsedSuccessfully` and `Ignored` are optional booleans; `RetryState` is `retryable`, `terminal`, or `resolved`; `Search` (max 200 characters) is trimmed and matched with `ILIKE` against raw title, feed name, or parsed title. Supplied filters are combined. |
| `ScanRunQuery` | `Status` is `running`, `succeeded`, `partial`, or `failed`; `Trigger` is `schedule`, `manual`, or `local`. Supplied filters are combined. |

Catalog titles with occurrences sort by `LastSeenAt` descending, then `Id` descending; Oscar films sort by film year descending, normalized title ascending, then `Id` ascending; occurrences use the catalog ordering. Oscar-only titles can still be opened through `GET /api/titles/{id}` when following an Oscar catalog link. Parse logs sort by `ProcessedAt` descending, then `Id` descending; scan runs sort by `StartedAt` descending, then `Id` descending. `PageResponse<T>` contains `Items`, `Page`, `PageSize`, `TotalCount`, and `TotalPages`; `TotalPages` is zero for an empty result and otherwise the ceiling of `TotalCount / PageSize`.

## Request DTOs

| DTO | Fields and validation |
| --- | --- |
| `CreateSourceRequest` | `StableKey` is required, max 100, and matches `^[a-z0-9][a-z0-9._-]{0,99}$`; `Name` is required, max 200; `FeedType` is required and is `movie` or `series`; `Url` is required, `[Url]`, max 2048; `IsEnabled` defaults to `true`. In addition to `[Url]`, the service accepts only whitespace-free absolute HTTPS URLs without user-info on `feed.rutracker.cc`. Text inputs are trimmed before persistence. |
| `UpdateSourceRequest` | Same `StableKey`, `Name`, `FeedType`, and `Url` validation as create; `IsEnabled` is a `bool` with no initializer (an omitted JSON value therefore defaults to `false`). The request replaces the source configuration. |
| `UpdateSettingsRequest` | `ExcludedGenres` and `ExcludedCountries` are required arrays of at most 100 values. Each value must be nonblank and at most 100 characters; values are trimmed, de-duplicated case-insensitively, and sorted case-insensitively. `MinMovieRating` and `MinSeriesRating` are each `0..10`; `MinImdbVotes` is `0..1,000,000,000`. |
| `UpdateProviderSettingsRequest` | `OmdbApiKey` is optional and max 512 characters; blank/omitted preserves the saved key. Set `ClearOmdbApiKey` to remove it, and do not provide a new key in the same request. The shared request limit and Oscar daily limit are non-negative integers; the per-run Oscar film cap is `0..100,000`. A configured key requires a positive shared limit; a positive Oscar film cap requires a positive Oscar daily cap. |

## Response DTOs

Nullable response fields are marked `?`; collection fields are returned as lists/arrays.

| DTO | Fields |
| --- | --- |
| `VersionResponse` | `Version` (`YYYY.MM.DD+<7-character-SHA>` for automated deployments), `CommitSha`, `CommitDateUtc?` |
| `HealthResponse` | `Status` |
| `PageResponse<T>` | `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages` |
| `CatalogTitleResponse` | `Id`, `Title`, `Year?`, `MediaType`, `SourceType?`, `ContentKind?`, `ImdbRating?`, `PosterUrl?`, `Genres`, `Countries`, `LastSeenAt?`, `OccurrenceCount` |
| `OscarFilmResponse` | `Id`, `TitleId`, `Title` (CSV title), `MetadataTitle`, `MetadataYear?`, `FilmYear`, `ImdbId?`, `EnrichmentStatus`, `EnrichmentAttemptCount`, `LastEnrichmentAttemptAt?`, `NextEnrichmentAttemptAt?`, `LastEnrichmentError?`, `MediaType`, `ImdbRating?`, `ImdbVotes?`, `Metascore?`, `Genres`, `Countries`, `Director?`, `Plot?`, `PosterUrl?`, `Runtime?`, `Awards?`, `BoxOffice?`, `Nominations` |
| `OscarNominationResponse` | `Id`, `Ceremony`, `Class`, `CanonicalCategory`, `Category`, `Name`, `Nominees`, `NomineeIds`, `Detail`, `IsWinner` |
| `TitleDetailsResponse` | `Id`, `Title`, `Year?`, `MediaType`, `SourceType?`, `ContentKind?`, `BroadcastRangeStartYear?`, `BroadcastRangeEndYear?`, `BroadcastRangeRaw?`, `ImdbId?`, `ImdbRating?`, `ImdbVotes?`, `Metascore?`, `Genres`, `Countries`, `Director?`, `Plot?`, `PosterUrl?`, `Runtime?`, `Awards?`, `BoxOffice?`, `FirstSeenAt?`, `LastSeenAt?`, `UpdatedAt`, `OccurrenceCount` |
| `OccurrenceResponse` | `Id`, `TitleId`, `SourceId`, `SourceName`, `SourceItemKey`, `FeedEntryId?`, `TorrentUrl`, `RawTitle`, `SourceFeedName`, `FeedType?`, `SourcePublishedAt?`, `ObservedAt?`, `Quality?`, `RipType?`, `FirstSeenAt`, `LastSeenAt` |
| `SourceResponse` | `Id`, `StableKey`, `Name`, `FeedType`, `Url`, `IsEnabled` |
| `SettingsResponse` | `ExcludedGenres`, `ExcludedCountries`, `MinMovieRating`, `MinSeriesRating`, `MinImdbVotes`, `UpdatedAt?` |
| `ProviderSettingsResponse` | `OmdbApiKeyConfigured`, `OmdbDailyRequestLimit`, `OscarEnrichmentMaxFilmsPerRun`, `OscarEnrichmentMaxRequestsPerDay`, `UpdatedAt?`; does not include the key. |
| `ParseLogResponse` | `Id`, `SourceId?`, `SourceName?`, `SourceItemKey?`, `RawTitle`, `FeedName`, `ParsedSuccessfully`, `ParsedTitle?`, `ParsedYear?`, `OmdbStatus`, `Ignored`, `IgnoreReason?`, `ErrorMessage?`, `Decision?`, `ProcessedAt`, `RetryState`, `AttemptCount`, `LastAttemptAt?`, `FeedType?`, `SourcePublishedAt?`, `ObservedAt?`, `EventKind?` |
| `ScanRunResponse` | `Id`, `StartedAt`, `FinishedAt?`, `Status`, `Trigger`, `FeedsProcessed`, `EntriesSeen`, `TitlesCreated`, `OccurrencesCreated`, `CacheHits`, `OmdbRequests`, `IgnoredEntries`, `ErrorCount`, `ErrorSummary` |
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

`Program.cs` does not register authentication or authorization middleware. Source and provider-settings writes and background-job producer endpoints are unauthenticated and LAN-trusted; do not infer an authentication contract from these routes. The provider settings response never returns the saved API key. Job endpoints expose no upload bytes, raw exception text, stack trace, provider request URL, or OMDb key.