# API: Operations and Jobs

Canonical contracts for health/version, application configuration, deployment, operational history, ingestion, and background jobs. Shared paging, errors, and access modes are in [API contracts](API_CONTRACTS.md); cross-layer flows are in [Architecture](ARCHITECTURE.md).

## Routes

Unless noted otherwise, configuration, operational-history, background-job, and deployment routes require an active administrator when access enforcement is enabled. In open mode they are unprotected and must remain within the configured trusted-network boundary.

| Method and route | Request | Success | Other documented outcomes |
| --- | --- | --- | --- |
| `GET /health/live` | None | `200 HealthResponse` (`Status = "ok"`) | Does not query PostgreSQL. |
| `GET /health/ready` | None | `200 HealthResponse` (`Status = "ready"`) when PostgreSQL is reachable | `503 ProblemDetails` when `CanConnectAsync` is false. |
| `GET /api/version` | None | `200 VersionResponse` with version, full source commit SHA, and UTC commit timestamp embedded in the image | Does not query PostgreSQL. |
| `GET /api/deployment` | None | `200 DeploymentStatusResponse` with systemd state, deployed SHA, failed-gate/recovery markers, and up to 40 recent service output lines | `503 ProblemDetails` when the host control socket is unavailable. |
| `POST /api/deployment/run` | `{ "action": "check" }` or `{ "action": "retry_failed_gate" }` | `202 DeploymentActionResponse` after queueing the fixed systemd deploy unit | `400 ProblemDetails` for unknown actions; `409 ProblemDetails` if already running, no failed gate exists, or recovery is required; `503 ProblemDetails` if the host control socket is unavailable. Retry removes only `gate-failed`; it never clears `deploy-failed`. |
| `GET /api/sources` | None | `200 IReadOnlyList<SourceProfileResponse>` in fixed `movie`, `series_complete`, `series_ongoing` order, each with configured URLs | None declared. |
| `POST /api/sources/{profileId}/urls` | Route profile ID, [SourceUrlRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `201 SourceUrlResponse` | `400 ValidationProblemDetails` for an unknown profile or invalid URL; `409 ProblemDetails` for a URL already assigned/configured. |
| `PUT /api/sources/{profileId}/urls/{id:long}` | Route profile ID and URL ID, [SourceUrlRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 SourceUrlResponse` | `400 ValidationProblemDetails` for an unknown profile or invalid URL; `404 ProblemDetails` when the URL is not active in that profile; `409 ProblemDetails` for a duplicate URL. |
| `DELETE /api/sources/{profileId}/urls/{id:long}` | Route profile ID and URL ID | `204` | `400 ValidationProblemDetails` for an unknown profile; `404 ProblemDetails` when the URL is not active in that profile. The row is disabled to retain occurrence/history foreign keys. |
| `GET /api/settings` | None | `200 SettingsResponse` | None declared. |
| `PUT /api/settings` | [UpdateSettingsRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 SettingsResponse` | `400 ValidationProblemDetails`. |
| `GET /api/settings/providers/omdb` | None | `200 ProviderSettingsResponse`; returns `OmdbApiKeyConfigured` and limits, never the key | None declared. |
| `GET /api/settings/providers/omdb/usage` | None | `200 OmdbDailyUsageResponse[]` for the last 30 UTC days, newest first; returns aggregate counts and safe error codes only | None declared. |
| `POST /api/settings/providers/omdb/test` | Empty body | `200 OmdbDiagnosticResponse` after exactly one request for fixed IMDb ID `tt16311594`; does not check or consume the MediaDock daily budget | `409 ProblemDetails` if no API key is configured. This route is LAN-trusted. |
| `PUT /api/settings/providers/omdb` | [UpdateProviderSettingsRequest](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) | `200 ProviderSettingsResponse` | `400 ValidationProblemDetails`; unauthenticated in open mode and LAN-trusted. |
| `GET /api/background-jobs/active` | None | `200 BackgroundJobResponse` or `204` when no job is queued/running | None declared. |
| `POST /api/background-jobs/scans` | Empty body | `202 BackgroundJobAcceptedResponse` with job ID and status URL | `409 ProblemDetails` with `activeJobId` and `activeJobStatusUrl` if an RSS scan is already queued/running. |
| `POST /api/background-jobs/recheck-failed` | Empty body | `202 BackgroundJobAcceptedResponse`; queues the latest `retryable` item for each source/item key and identity-less legacy failures | `409 ProblemDetails` with active-job details if an RSS scan or recheck is already queued/running. |
| `POST /api/background-jobs/oscar-enrichment` | Empty body | `202 BackgroundJobAcceptedResponse` with job ID and status URL | `409 ProblemDetails` if an Oscar enrichment job is already queued/running. |
| `POST /api/background-jobs/oscar-import` | Multipart `File` (CSV/TSV) and optional `YearAfter` | `202 BackgroundJobAcceptedResponse` | `400 ValidationProblemDetails` for empty/oversized file, unsupported filename/content type, or invalid year. Client paths are not accepted. |
| `POST /api/background-jobs/golden-globe-enrichment` | Empty body | `202 BackgroundJobAcceptedResponse` | `409 ProblemDetails` if a Golden Globes enrichment job is already queued or running. |
| `POST /api/background-jobs/golden-globe-import` | Multipart `File` (CSV/TSV) and optional `YearAfter` | `202 BackgroundJobAcceptedResponse` | `400 ValidationProblemDetails` for empty/oversized file, unsupported filename extension, or invalid year. Client paths are not accepted. |
| `GET /api/background-jobs/{id:long}` | Job ID | `200 BackgroundJobResponse` with lifecycle, progress, safe summary/error code, and optional `scanRunId` | `404 ProblemDetails` for unknown ID. |
| `GET /api/background-jobs/{id:long}/events` | `afterId` (default 0), `pageSize` (default 50, max 100) | `200 BackgroundJobEventsResponse` ordered by event ID | `400 ValidationProblemDetails`; `404 ProblemDetails` for unknown job. |
| `GET /api/parse-logs` | [ParseLogQuery](../../server/src/MediaDock.Api/Operations/OperationsContracts.cs) | `200 PageResponse<ParseLogResponse>` | `400 ValidationProblemDetails` for invalid query values. |
| `GET /api/scan-runs` | [ScanRunQuery](../../server/src/MediaDock.Api/Operations/OperationsContracts.cs) | `200 PageResponse<ScanRunResponse>` | `400 ValidationProblemDetails` for invalid query values. This lists history; it does not start a scan. |
| `GET /openapi/v1.json` | None | OpenAPI document in Development only | `MapOpenApi` is registered only in Development. |

Deployment status and start routes proxy a fixed request set to the host-side Unix-socket service. They accept no commands, paths, or unit names from clients and must only be reachable inside the trusted LAN boundary.

## Queries And Validation

Paged queries use the shared page limits and defaults in [API contracts](API_CONTRACTS.md#paging). Supplied filters are combined.

| Query DTO | Additional fields and behavior |
| --- | --- |
| `ParseLogQuery` | `SourceId` and `ScanRunId` must be at least 1; `ParsedSuccessfully` and `Ignored` are optional booleans; `RetryState` is `retryable`, `terminal`, or `resolved`; `Search` (max 200 characters) is trimmed and matched with `ILIKE` against raw title, feed name, or parsed title. |
| `ScanRunQuery` | `Status` is `running`, `succeeded`, `partial`, or `failed`; `Trigger` is `schedule`, `manual`, or `local`. |

Parse logs sort by `ProcessedAt` descending, then `Id` descending; scan runs sort by `StartedAt` descending, then `Id` descending.

`SourceUrlRequest.Url` is required, `[Url]`, and at most 2048 characters. The service trims outer whitespace, then accepts only whitespace-free absolute HTTPS URLs without user-info on `feed.rutracker.cc`. The route profile ID is authoritative; the request has no name, type, key, enable flag, or parser option. The profile ID is a fixed system value and the only selector for RSS behavior: `movie` uses movie parsing, OMDb `type=movie`, and parsed release year for lookup/matching; `series_complete` and `series_ongoing` use OMDb `type=series` without `y` and have separate season-pack/episode-marker parsing rules. A parsed season/episode year is retained in parse history but is not used as a show's premiere year. Legacy `series` records migrate to `series_ongoing`.

`GoldenGlobeImportForm` accepts a required non-empty `.csv` or `.tsv` within the configured background-job upload limit. Optional `YearAfter` defaults to 1980 and accepts `0..9998`; parsing is asynchronous after queueing.

## Request DTOs

| DTO | Fields and validation |
| --- | --- |
| `UpdateSettingsRequest` | `ExcludedGenres` and `ExcludedCountries` are required arrays of at most 100 values. Each value must be nonblank and at most 100 characters; values are trimmed, de-duplicated case-insensitively, and sorted case-insensitively. `MinMovieRating` and `MinSeriesRating` are each `0..10`; `MinImdbVotes` is `0..1,000,000,000`. |
| `UpdateProviderSettingsRequest` | `OmdbApiKey` is optional and max 512 characters; blank/omitted preserves the saved key. Set `ClearOmdbApiKey` to remove it, and do not provide a new key in the same request. The shared daily request limit is a non-negative integer; a configured key requires a positive value. |
| `DeploymentActionRequest` | `Action` is `check` or `retry_failed_gate`. |

## Response DTOs

Nullable response fields are marked `?`; collection fields are lists/arrays.

| DTO | Fields |
| --- | --- |
| `VersionResponse` | `Version` (`YYYY.MM.DD+<7-character-SHA>` for automated deployments), `CommitSha`, `CommitDateUtc?` |
| `HealthResponse` | `Status` |
| `DeploymentStatusResponse` | `IsRunning`, `ActiveState`, `SubState`, `Result`, `ExitCode`, `StartedAt`, `FinishedAt`, `DeployedSha?`, `GateFailedSha?`, `RecoveryRequired`, `FailureTargetSha?`, `RecentOutput` (up to 40 lines) |
| `DeploymentActionResponse` | `Message` |
| `SourceProfileResponse` | `Id` (fixed profile ID), `Name` (fixed display label), `Urls` |
| `SourceUrlResponse` | `Id`, `Url` |
| `SettingsResponse` | `ExcludedGenres`, `ExcludedCountries`, `MinMovieRating`, `MinSeriesRating`, `MinImdbVotes`, `UpdatedAt?` |
| `ProviderSettingsResponse` | `OmdbApiKeyConfigured`, `OmdbDailyRequestLimit`, `UpdatedAt?`; does not include the key. |
| `OmdbDiagnosticResponse` | `ImdbId`, `Status`, `IsSuccessful`, `Message` (original provider error message when returned), `Title?`, `Year?`, `HttpAttempts`; never includes the API key or provider request URL. |
| `OmdbDailyUsageResponse` | `UtcDate`, `TotalRequests`, `RssRequests`, `OscarRequests`, `DailyRequestLimitReached`, `ProviderQuotaExceeded`, `LastErrorCode?`; counts are conservative reservations and can include an attempt interrupted before sending. |
| `ParseLogResponse` | `Id`, `SourceId?`, `SourceName?`, `SourceItemKey?`, `RawTitle`, `FeedName`, `ParsedSuccessfully`, `ParsedTitle?`, `ParsedYear?`, `LookupTitles` (ordered names sent to metadata lookup; empty when parsing or validation stopped before lookup), `OmdbStatus`, `Ignored`, `IgnoreReason?`, `ErrorMessage?`, `Decision?`, `ProcessedAt`, `RetryState`, `AttemptCount`, `LastAttemptAt?`, `FeedType?`, `SourcePublishedAt?`, `ObservedAt?`, `EventKind?` |
| `ScanRunResponse` | `Id`, `StartedAt`, `FinishedAt?`, `Status`, `Trigger`, `FeedsProcessed`, `EntriesSeen`, `KnownEntriesSkipped`, `TitlesCreated`, `OccurrencesCreated`, `CacheHits`, `OmdbRequests` (actual HTTP attempts), `IgnoredEntries`, `ErrorCount`, `ErrorSummary` |
| `BackgroundJobResponse` | `Id`, `JobType`, `Trigger`, `Status`, `EnqueuedAt`, `StartedAt?`, `FinishedAt?`, `CurrentStage?`, `CurrentSource?`, `ProgressUpdatedAt?`, `ErrorCode?`, `ResultSummary?`, `ScanRunId?`, `InputFileName?`; never contains CSV bytes, credentials, stack traces, or provider URLs. |
| `BackgroundJobAcceptedResponse` | `Id`, `Status`, `StatusUrl` |
| `BackgroundJobEventResponse` | `Id`, `OccurredAt`, `Level`, `EventCode`, `Message`, `Data?`; event fields are safe, bounded projections. |
| `BackgroundJobEventsResponse` | `Items`, `NextAfterId` |

When no settings row exists, `GET /api/settings` returns empty exclusion arrays, zero thresholds, and `UpdatedAt = null`; writes create or update the singleton row with `id = 1`, and concurrent first initialization resolves to that row. `GET /api/settings/providers/omdb` returns `OmdbApiKeyConfigured = false` and zero limits when no settings row exists.

The OMDb diagnostic route sends one request for fixed ID `tt16311594`; it does not check or consume the MediaDock daily request budget. Usage returns the last 30 UTC days, newest first, with aggregate counts and safe error codes. Provider settings never return the saved key.

## Background Job Behavior

Job types are `rss_scan` (manual or scheduled; manual jobs can be full scans or failed-entry rechecks), `oscar_enrichment`, `oscar_import`, `golden_globe_enrichment`, `golden_globe_import`, `golden_globe_manual_refresh`, and `imdb_title_enrichment` (all except RSS are manual). IMDb-title enrichment is scoped to one owner and deduplicated per owner while active; global job status and events expose no personal rating IDs or scores.

Legacy failed RSS entries are retried only when a currently enabled feed has one unique title match that also satisfies any stored feed-entry ID or torrent URL; unavailable or ambiguous matches are reported in the job summary. A manual Golden Globes refresh targets one exact group and IMDb ID and reuses the API dispatcher, advisory lock, metadata cache, and shared OMDb budget. RSS scans do not run award-catalog imports or enrichment as follow-up operations.

## Implementation And Tests

Endpoint and DTO definitions: [HealthEndpoints.cs](../../server/src/MediaDock.Api/Health/HealthEndpoints.cs), [VersionEndpoints.cs](../../server/src/MediaDock.Api/Versioning/VersionEndpoints.cs), [SourceSettingsEndpoints.cs](../../server/src/MediaDock.Api/Sources/SourceSettingsEndpoints.cs), [OperationalHistoryEndpoints.cs](../../server/src/MediaDock.Api/Operations/OperationalHistoryEndpoints.cs), [DeploymentControlEndpoints.cs](../../server/src/MediaDock.Api/Operations/DeploymentControlEndpoints.cs), and [BackgroundJobEndpoints.cs](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobEndpoints.cs). Relevant API coverage: [CatalogApiTests.cs](../../server/tests/MediaDock.IntegrationTests/CatalogApiTests.cs), [BackgroundJobApiTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobApiTests.cs), [BackgroundJobExecutionTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs), and [DeploymentControlApiTests.cs](../../server/tests/MediaDock.IntegrationTests/DeploymentControlApiTests.cs).