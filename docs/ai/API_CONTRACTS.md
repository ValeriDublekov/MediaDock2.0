# HTTP API Contracts

This page is the shared-conventions router for the API in `server/`. Routes are registered in [Program.cs](../../server/src/MediaDock.Api/Program.cs); see [Architecture](ARCHITECTURE.md) for cross-layer request flow.

| API area | Canonical contract |
| --- | --- |
| Catalog, Oscars, Golden Globes, and award recognition | [Catalogs and awards](API_CATALOGS_AND_AWARDS.md) |
| Sign-in, account administration, favorites, and personal ratings | [Identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md) |
| Personal movie and series recommendations | [Identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md) and [recommendation plan](plans/movie-recommendations.md) |
| Health, configuration, operations, ingestion, and jobs | [Operations and jobs](API_OPERATIONS_AND_JOBS.md) |

Catalog IDs, Oscar record IDs, and Golden Globes film IDs belong to their respective source-owned records. Cross-source Favorites state is keyed by IMDb ID, not by one of those local IDs.

## Paging

Paged query DTOs accept optional `Page` in `1..1,000,000` and `PageSize` in `1..100`; omitted values are `1` and `25`. Invalid annotated values produce a 400 validation problem. Pagination uses offsets; a page past the end returns an empty `Items` list rather than being clamped.

`PageResponse<T>` contains `Items`, `Page`, `PageSize`, `TotalCount`, and `TotalPages`. `TotalPages` is zero for an empty result and otherwise the ceiling of `TotalCount / PageSize`. Catalog list responses additionally expose optional year and IMDb-rating bounds; see [catalog and award contracts](API_CATALOGS_AND_AWARDS.md) for their sources and filtering behavior. Recommendation pages expose year bounds from the active user's eligible candidates before recommendation filters are applied.

`GET /api/recommendations` uses the same page envelope, defaults to page `1` and page size `25`, and caps page size at `100`. Optional `mediaType` selects `movie` or `series` and defaults to `movie`. Optional inclusive `yearFrom`/`yearTo` and repeatable `genres` filters apply before counting and paging; a title matches when it has any selected genre. Every request also requires `ratingWeight`, `votesWeight`, and `awardsWeight`; each must be in `[0,1]` and their sum must be `1.0`. `GET /api/recommendations/genres?mediaType=...` returns distinct genres for that title type and defaults to `movie`. Recommendation settings and vote recalculation use separate administrator routes; see [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md) for owner resolution, settings DTOs, and freshness behavior.

## Errors

[ApiExceptionHandler.cs](../../server/src/MediaDock.Api/Middleware/ApiExceptionHandler.cs) maps `ApiValidationException` to 400 `ValidationProblemDetails`, `ApiForbiddenException` to 403 `ProblemDetails`, `ApiNotFoundException` to 404 `ProblemDetails`, and `ApiConflictException` to 409 `ProblemDetails`. Handled exceptions use `application/problem+json`, include `traceId` in the body, and set `X-MediaDock-TraceId` in the response header. Unrecognized exceptions return 500 `ProblemDetails`; readiness failure is a separate 503 response.

Authentication session, anti-forgery, and logout responses also include `X-MediaDock-TraceId`. Authentication logs record safe rejection reasons and trace IDs, not identity claims, session cookies, or anti-forgery tokens. Cookie challenges are API-native 401 responses and forbids are 403, not login redirects.

## Access Modes

`ACCESS_CONTROL_ENFORCED` defaults to `false` for Development. A normal Production API startup requires Google sign-in to be enabled with valid client credentials and callback configuration, and requires `AccessControl:Enforced=true`; otherwise startup fails. When enforcement is enabled, protected routes resolve the validated Google identity to an active linked account and current database role on each request. Shared catalog and award reads require an active `user` or `admin`; global configuration, recommendation settings and vote recalculation, account administration, operational history, background jobs, deployment, and manual Golden Globes linking require an active `admin`. Favorites, personal ratings, and recommendations always require an active linked user and are scoped to that user, including when general enforcement is disabled in Development.

When enforcement is disabled in Development, authorization policies are bypassed for other routes and the rest of the API is explicitly unprotected; the UI retains its warning. The anonymous HTTP surface is limited to the Google sign-in/session, anti-forgery, link/registration, and logout flow plus the internal liveness/readiness probes. `/api/version` and application-data routes use the fallback policy. Identity-link and registration handlers still validate identity and anti-forgery tokens themselves. Anti-forgery validation is not authorization. Keep open Development mode within the configured loopback boundary; see [security](SECURITY.md).
