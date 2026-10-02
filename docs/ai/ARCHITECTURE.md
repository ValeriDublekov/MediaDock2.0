# Architecture and Runtime Flows

This guide covers the standalone React client, API-hosted background jobs, and PostgreSQL database. See [project context](PROJECT_CONTEXT.md) for the main directories and [local runbook](../../README.md) for operational commands.

## Responsibilities and Dependencies

Solid arrows show runtime traffic or calls; dashed arrows show .NET project references.

```mermaid
flowchart LR
    web["React web client"] -->|HTTP JSON| api["MediaDock.Api"]
    api -.->|ProjectReference| app["MediaDock.Application"]
    api -.->|ProjectReference| infra["MediaDock.Infrastructure"]
    infra -.->|ProjectReference; implements app ports| app
    api -->|"CatalogApiService uses EF DbContext"| infra
    api -->|"BackgroundJobDispatcher; scoped handlers"| app
    api -->|"job queue, scheduler, lock, adapters"| infra
    infra -->|"EF Core / Npgsql"| db[("PostgreSQL")]
```

- [Application](../../server/src/MediaDock.Application/MediaDock.Application.csproj) has no project references. It contains ingestion use cases and contracts, parsing, matching, and metadata-resolution logic.
- [Infrastructure](../../server/src/MediaDock.Infrastructure/MediaDock.Infrastructure.csproj) references Application. It supplies PostgreSQL/EF Core persistence and adapters implementing application contracts, including the [PostgreSQL ingestion repository](../../server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs).
- [API](../../server/src/MediaDock.Api/MediaDock.Api.csproj) references Application and Infrastructure and is the only ingestion runtime/composition root. Its HTTP endpoints enqueue requests; one hosted dispatcher claims and executes them.
- The [web client](../../web/src/api/client.ts) is a separate React/TypeScript project that talks to the API over HTTP; it does not reference .NET projects. The [server Dockerfile](../../server/Dockerfile) builds the web app and copies its static output into the API image. In production the API serves those files, so Compose has no separate web service.

The project references describe what each project can reference, not the path every request takes. In particular, the current catalog read path is implemented in the API and directly uses Infrastructure's `MediaDockDbContext`; it does not pass through an Application catalog use case.

## Catalog HTTP Flow

```mermaid
sequenceDiagram
    participant View as CatalogView
    participant Client as api/client.ts
    participant Route as CatalogEndpoints
    participant Service as CatalogApiService
    participant Db as MediaDockDbContext / PostgreSQL
    View->>Client: getCatalog(query)
    Client->>Route: GET /api/catalog?... via fetch
    Route->>Service: GetCatalogAsync(query)
    Service->>Db: EF Core filters, count, and page query
    Db-->>Service: rows and count
    Service-->>Route: PageResponse<CatalogTitleResponse>
    Route-->>Client: 200 JSON
    Client-->>View: typed page response
```

The [catalog view](../../web/src/features/catalog/CatalogView.tsx) builds the query and calls `getCatalog`. The [API client](../../web/src/api/client.ts) serializes query values, prefixes `/api`, and calls `fetch`; its TypeScript contracts live in [api/types.ts](../../web/src/api/types.ts). On the server, [API startup](../../server/src/MediaDock.Api/Program.cs) registers `ICatalogApiService` and maps routes from [CatalogEndpoints](../../server/src/MediaDock.Api/Catalog/CatalogEndpoints.cs). The endpoint delegates to [CatalogApiService](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs), which filters, counts, orders, pages, and projects EF queries over the Infrastructure [DbContext](../../server/src/MediaDock.Infrastructure/Persistence/MediaDockDbContext.cs). The HTTP query and response DTOs are in [CatalogContracts](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs).

Favorites follow the same direct API-to-EF pattern: [FavoriteApiService](../../server/src/MediaDock.Api/Favorites/FavoriteApiService.cs) owns filtering, page projection, source-checked atomic additions and independent marker updates. [FavoriteProvider](../../web/src/features/favorites/FavoriteContext.tsx) shares current favorites between catalogs, dialogs and the favorites screen; title detail requests separately load Oscar records and torrent occurrences by canonical `TitleId`.

For catalog behavior, start with the API endpoint, contract, or service that owns the change. Keep the current query there unless the code gains a real shared use case; do not add an Application abstraction just to make the layers look uniform.

## API-Hosted Ingestion Flow

```mermaid
flowchart TD
    ui["Configuration UI"] -->|"RSS scan / Oscar enrichment / CSV upload"| routes["BackgroundJobEndpoints"]
    scheduler["BackgroundJobDispatcher scheduler"] -->|"07:00 / 18:00 Europe/Sofia"| queue[("background_jobs")]
    routes -->|"durable enqueue"| queue
    queue -->|"atomic claim: FOR UPDATE SKIP LOCKED"| dispatcher["BackgroundJobDispatcher"]
    dispatcher --> lock["PostgresAdvisoryScanLock"]
    lock --> handler["RSS scan / Oscar enrichment / import handler"]
    handler --> app["Application use cases"]
    app --> infra["Infrastructure adapters"]
    infra --> db[("PostgreSQL")]
    dispatcher --> events[("background_job_events")]
    app --> history[("scan_runs / parse_logs")]
    ui -->|"status + cursor events"| routes
```

The API exposes producers and read-only status/event routes in
[BackgroundJobEndpoints](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobEndpoints.cs).
HTTP requests validate and persist work; they never run a scan inline. The one
hosted [BackgroundJobDispatcher](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobDispatcher.cs)
uses a scoped `MediaDockDbContext`, acquires the session advisory lock, claims
one job in a short transaction, then executes the handler while retaining the
lease. Queued jobs survive API restarts. Stale `running` jobs are marked
`interrupted` after the next dispatcher acquires the lock; they are not retried.

The [scheduler](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobScheduler.cs)
persists a checkpoint and UTC schedule slots. Its first activation initializes
the checkpoint without replaying historical slots; later downtime coalesces
missed due slots into one job. The 07:00/18:00 `Europe/Sofia` conversion is
deterministic across DST. One partial unique index prevents more than one
queued/running RSS job, while a unique slot index makes schedule enqueue
idempotent.

RSS and Oscar handlers load the OMDb key and shared daily limit from the singleton
`settings` row inside the job scope. The atomic budget allows requests up to that
configured limit, with no separate Oscar caps or safety buffer; an OMDb quota error
also blocks further requests for that UTC day. Credentials never enter job payloads
or API responses.
`RssIngestionService` reports stage/source/counter snapshots at feed boundaries
and periodically during processing. New parse logs store their `scan_run_id`;
older rows remain unassociated. Oscar enrichment is a separate manual job using
the same lock and resolver/cache/shared budget. CSV uploads are bounded bytes stored
with the queued import; the importer parses them without a host path and clears
the bytes at terminal state.

### Advisory-Lock Boundary

[PostgresAdvisoryScanLock](../../server/src/MediaDock.Infrastructure/Ingestion/PostgresAdvisoryScanLock.cs)
uses PostgreSQL session-level `pg_try_advisory_lock` and releases it with
`pg_advisory_unlock` when the async lease is disposed. The dispatcher holds the
lease across queue claim and one RSS scan, Oscar enrichment, or Oscar import.
If another cooperating runner holds the same lock, the job remains queued for
a later poll. The lock does not serialize ordinary API writes or migrations.

## Startup and Health

```mermaid
flowchart TD
    db["Compose db"] -->|"pg_isready healthcheck"| healthy["db healthy"]
    healthy --> api["api service"]
    healthy --> migrate["migrate service: tools profile"]
```

The [Compose file](../../compose.yaml) defines PostgreSQL (`db`), the API (`api`), and a one-shot `migrate` service. Both API and migration wait for the database healthcheck. The migration service uses the API image with `--migrate true`; [API startup](../../server/src/MediaDock.Api/Program.cs) applies EF Core migrations and exits before `app.Run`, so the hosted dispatcher does not start in migration mode. Normal API startup does not migrate, and Compose does not make `api` depend on `migrate`; the [runbook](../../README.md) documents the intended order.

API startup registers `MediaDockDbContext` with Npgsql from `ConnectionStrings:MediaDock`. `/health/live` reports process liveness; `/health/ready` calls `Database.CanConnectAsync` and returns 503 when PostgreSQL is unavailable, as implemented by [HealthEndpoints](../../server/src/MediaDock.Api/Health/HealthEndpoints.cs) and [ReadinessService](../../server/src/MediaDock.Api/Health/ReadinessService.cs). Compose configures a healthcheck for `db`, not an API healthcheck; the readiness endpoint is not currently wired as a Compose healthcheck.

The API image serves the bundled React app only in Production, via the default/static-file middleware and SPA fallback in `Program.cs`; its assets are produced by the [server Dockerfile](../../server/Dockerfile). Compose binds the API host port to loopback. This is an unauthenticated local MVP; keep the deployment boundary described in [project context](PROJECT_CONTEXT.md).

## Where to Change Things

| Change | Start here |
| --- | --- |
| HTTP route, validation, response contract, or catalog query | [CatalogEndpoints](../../server/src/MediaDock.Api/Catalog/CatalogEndpoints.cs), [CatalogContracts](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs), [CatalogApiService](../../server/src/MediaDock.Api/Catalog/CatalogApiService.cs), and [API Program](../../server/src/MediaDock.Api/Program.cs) |
| React screen, HTTP serialization, or browser types | [CatalogView](../../web/src/features/catalog/CatalogView.tsx), [API client](../../web/src/api/client.ts), and [api/types.ts](../../web/src/api/types.ts) |
| Feed parsing, ingestion sequencing, matching, or metadata-resolution behavior | [RssIngestionService](../../server/src/MediaDock.Application/Ingestion/RssIngestionService.cs), [ingestion contracts](../../server/src/MediaDock.Application/Ingestion/RssIngestionContracts.cs), [RutrackerTitleParser](../../server/src/MediaDock.Application/Parsing/RutrackerTitleParser.cs), [MatchPolicy](../../server/src/MediaDock.Application/Matching/MatchPolicy.cs), and [MetadataResolver](../../server/src/MediaDock.Application/Metadata/MetadataResolver.cs) |
| Oscar CSV import, nominated-film persistence, or category filtering | [BackgroundJobApiService](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobApiService.cs), [OscarDatasetImporter](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarDatasetImporter.cs), [OscarCsvDatasetReader](../../server/src/MediaDock.Infrastructure/OscarAwards/OscarCsvDatasetReader.cs), and Oscar persistence entities/configurations |
| PostgreSQL schema, EF mapping, ingestion persistence, or provider adapters | Infrastructure persistence beginning at [MediaDockDbContext](../../server/src/MediaDock.Infrastructure/Persistence/MediaDockDbContext.cs), [PostgresRssIngestionRepository](../../server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs), [PostgresMetadataCacheStore](../../server/src/MediaDock.Infrastructure/Metadata/PostgresMetadataCacheStore.cs), [OmdbClient](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs), and [RssFeedTransportAdapter](../../server/src/MediaDock.Infrastructure/Ingestion/RssFeedTransportAdapter.cs); use the [local runbook](../../README.md) for migration order |
| Job persistence, atomic claim, schedule, progress, or recovery | [BackgroundJobDispatcher](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobDispatcher.cs), [BackgroundJobScheduler](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobScheduler.cs), and [BackgroundJobConfigurations](../../server/src/MediaDock.Infrastructure/Persistence/Configurations/BackgroundJobConfigurations.cs) |
| Image targets, service startup, or local operational procedure | [server Dockerfile](../../server/Dockerfile), [Compose](../../compose.yaml), and the [local runbook](../../README.md) |