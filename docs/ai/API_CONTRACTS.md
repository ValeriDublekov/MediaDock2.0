# HTTP API Contracts

This page is the shared-conventions router for the API in `server/`. Routes are registered in [Program.cs](../../server/src/MediaDock.Api/Program.cs); see [Architecture](ARCHITECTURE.md) for cross-layer request flow.

| API area | Canonical contract |
| --- | --- |
| Catalog, Oscars, Golden Globes, and award recognition | [Catalogs and awards](API_CATALOGS_AND_AWARDS.md) |
| Sign-in, account administration, favorites, and personal ratings | [Identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md) |
| Health, configuration, operations, ingestion, and jobs | [Operations and jobs](API_OPERATIONS_AND_JOBS.md) |

## Paging

Paged query DTOs accept optional `Page` in `1..1,000,000` and `PageSize` in `1..100`; omitted values are `1` and `25`. Invalid annotated values produce a 400 validation problem. Pagination uses offsets; a page past the end returns an empty `Items` list rather than being clamped.

`PageResponse<T>` contains `Items`, `Page`, `PageSize`, `TotalCount`, and `TotalPages`. `TotalPages` is zero for an empty result and otherwise the ceiling of `TotalCount / PageSize`. Catalog list responses additionally expose optional year and IMDb-rating bounds; see [catalog and award contracts](API_CATALOGS_AND_AWARDS.md) for their sources and filtering behavior.

## Errors

[ApiExceptionHandler.cs](../../server/src/MediaDock.Api/Middleware/ApiExceptionHandler.cs) maps `ApiValidationException` to 400 `ValidationProblemDetails`, `ApiForbiddenException` to 403 `ProblemDetails`, `ApiNotFoundException` to 404 `ProblemDetails`, and `ApiConflictException` to 409 `ProblemDetails`. Handled exceptions use `application/problem+json`, include `traceId` in the body, and set `X-MediaDock-TraceId` in the response header. Unrecognized exceptions return 500 `ProblemDetails`; readiness failure is a separate 503 response.

Authentication session, anti-forgery, and logout responses also include `X-MediaDock-TraceId`. Authentication logs record safe rejection reasons and trace IDs, not identity claims, session cookies, or anti-forgery tokens. Cookie challenges are API-native 401 responses and forbids are 403, not login redirects.

## Access Modes

`ACCESS_CONTROL_ENFORCED` defaults to `false`. When enabled, protected routes resolve the validated Google identity to an active linked account and current database role on each request. Shared catalog and award reads require an active `user` or `admin`; global configuration, account administration, operational history, background jobs, deployment, and manual Golden Globes linking require an active `admin`. Favorites and personal ratings always require an active linked user and are scoped to that user, including when general enforcement is disabled.

When enforcement is disabled, authorization policies are bypassed for other routes and the rest of the API is explicitly unprotected; the UI retains its warning. Health, version, and authentication-protocol routes are policy-anonymous, although identity-link and registration handlers still validate identity and anti-forgery tokens themselves. Anti-forgery validation is not authorization. Keep open mode within the configured loopback or trusted-network boundary; see [security](SECURITY.md).
