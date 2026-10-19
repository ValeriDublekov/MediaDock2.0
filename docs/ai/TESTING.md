# Development and Test Strategy

This guide covers testing for the standalone application in this repository. See the [AI documentation index](README.md), [architecture](ARCHITECTURE.md), and [local runbook](../../README.md).

## Prerequisites

- .NET 10 SDK for the server solution and its `net10.0` projects.
- Node.js and npm for the React/Vite project. The committed [package lock](../../web/package-lock.json) is used by `npm ci`.
- A running Docker Engine for PostgreSQL integration tests. The integration project uses Testcontainers to start disposable `postgres:17-alpine` containers; no pre-existing database or Compose stack is required. Docker must be able to use that image, pulling it if it is not already available locally.

The .NET unit tests and web tests do not require PostgreSQL, Docker, RSS sources, or OMDb credentials.

## Compact Test Runner

Run the standard-library Python runner from the repository root. It suppresses passing-test output, summarizes failures from .NET TRX and Vitest JSON reports, and keeps full logs and reports under `.test-tmp/` by default. Each run gets a unique `mediadock-tests-*` directory; artifacts are retained for diagnosis. Use `--artifacts-dir <path>` to choose a different base directory (relative paths are resolved from the repository root).

```powershell
python -B scripts/run_tests.py all
python -B scripts/run_tests.py server-unit --filter "Category=Parsing"
python -B scripts/run_tests.py server-integration --filter "Category=Api"
python -B scripts/run_tests.py web --filter CatalogView
python -B scripts/run_tests.py deploy
python -B scripts/run_tests.py runner
```

`all` runs both .NET projects, web tests, deploy-control tests, and the runner's own parser tests; integration tests still require Docker. `--filter` applies to one selected suite: it uses the VSTest filter expression for .NET, a Vitest file-path filter for web, or Python unittest's `-k` name pattern. .NET restore is skipped by default; pass `--restore` when package restore is needed. The runner exits nonzero if any selected suite fails.

Each test process's stdout and stderr are captured in its `.log`; raw output is not streamed to the terminal. Only the `server-integration` child process receives `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning`, filtering EF Core database-command Information logs while retaining Warning and Error events. This does not change production logging configuration.

Failure summaries show at most 12 failures and 16 lines per failure. If a structured test report is missing, the runner prints only recognized, bounded compiler/test diagnostics. If it finds none, it reports the exit status and full log path rather than dumping an arbitrary log tail.

## File Line Inventory

Use the inventory to identify large text files for review; line count alone is not a reason to refactor. Results are sorted by descending count. Binary files and known generated, dependency, cache, and test-artifact directories are skipped.

```powershell
python -B scripts/report_file_lines.py --min-lines 300
python -B scripts/report_file_lines.py
```

`--min-lines 300` shows files with at least 300 lines. Omitting the option lists every eligible text file.

## Restore and Build

Run server commands from the repository root. The [solution](../../server/MediaDock.sln) includes the API, Application, Infrastructure, and both test projects.

```powershell
dotnet restore server/MediaDock.sln
dotnet build server/MediaDock.sln --no-restore
```

Run web commands from `web`:

```powershell
npm ci
npm run lint
npm run test
npm run build
```

The [web package scripts](../../web/package.json) define `lint` as Oxlint, `test` as Vitest with jsdom, and `build` as TypeScript project build followed by Vite build. The web tests use mocked API calls, so they need no running API or database; see the [API client tests](../../web/src/api/client.test.ts), [catalog view tests](../../web/src/features/catalog/CatalogView.test.tsx), [Golden Globes catalog test](../../web/src/features/golden-globes/GoldenGlobeCatalogView.test.tsx), [configuration view test](../../web/src/features/sources/SourceSettingsView.test.tsx), and [ingestion panel tests](../../web/src/features/sources/BackgroundIngestionPanel.test.tsx), which include Golden Globes job controls.

The host deployment-control bridge uses only Python's standard library and can
be checked without systemd or Docker:

```powershell
python -B scripts/run_tests.py deploy
```

## .NET Unit Tests

Run from the repository root after solution restore. The [unit-test project](../../server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj) has focused xUnit categories:

| Category | Coverage and test data |
| --- | --- |
| `Parsing` | Title parsing edge cases with inline sample titles in [RutrackerTitleParserTests](../../server/tests/MediaDock.UnitTests/RutrackerTitleParserTests.cs). |
| `RssTransport` | RSS/Atom parsing and URL, DNS, redirect, and size limits using inline XML, `FakeHttpHandler`, and `FakeDnsResolver` in [RssFeedTransportTests](../../server/tests/MediaDock.UnitTests/RssFeedTransportTests.cs). |
| `Matching` | Type, year, exclusion, and broadcast-range decisions using inline values and xUnit data in [MatchPolicyTests](../../server/tests/MediaDock.UnitTests/MatchPolicyTests.cs). |

[`OscarEnrichmentServiceTests`](../../server/tests/MediaDock.UnitTests/OscarEnrichmentServiceTests.cs) verifies completed/partial/quota-stopped/failed run outcomes, retry state, and HTTP attempt counts with stubbed metadata lookups.
[`GoldenGlobeEnrichmentServiceTests`](../../server/tests/MediaDock.UnitTests/GoldenGlobeEnrichmentServiceTests.cs) verifies ceremony-year matching, terminal outcomes, exponential retries, and provider/local quota behavior with stubbed metadata lookups.

```powershell
python -B scripts/run_tests.py server-unit
python -B scripts/run_tests.py server-unit --filter "Category=Parsing"
python -B scripts/run_tests.py server-unit --filter "Category=RssTransport"
python -B scripts/run_tests.py server-unit --filter "Category=Matching"
python -B scripts/run_tests.py server-unit --filter "Category=GoldenGlobes"
```

## PostgreSQL Integration Tests

Run from the repository root after solution restore. These tests require Docker because they start their own PostgreSQL 17 containers; they do not use the Compose database. The [integration-test project](../../server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj) has these xUnit categories:

| Category | Coverage and test data |
| --- | --- |
| `Persistence` | Applies fresh migrations and checks schema/occurrence uniqueness in [PersistenceTests](../../server/tests/MediaDock.IntegrationTests/PersistenceTests.cs). `SharedImdbMigrationBackfillsNormalizedIdsAndPreservesLocalMetadata` starts from the populated pre-migration schema and verifies normalized/deduplicated IMDb IDs, latest title metadata backfill, preserved no-ID title metadata and per-owner ratings, award references, and required/restrictive foreign keys. [IdentityPersistenceTests](../../server/tests/MediaDock.IntegrationTests/IdentityPersistenceTests.cs) verifies the ownership migration's zero-user deletion, one-user backfill, multi-user copy (including deactivated accounts), preserved values/timestamps, composite keys, and restrictive user deletion. Golden Globes tests cover CSV/TSV import idempotency, candidate eligibility, and grouped-outcome persistence in [GoldenGlobeDatasetImporterTests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeDatasetImporterTests.cs) and [GoldenGlobeEnrichmentRepositoryTests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeEnrichmentRepositoryTests.cs). Oscar tests also verify import/enrichment persistence and the separate run-audit lifecycle in [OscarDatasetImporterTests](../../server/tests/MediaDock.IntegrationTests/OscarDatasetImporterTests.cs) and [OscarEnrichmentRepositoryTests](../../server/tests/MediaDock.IntegrationTests/OscarEnrichmentRepositoryTests.cs). |
| `Ingestion` | Checks idempotency, metadata caching, and partial failures against PostgreSQL. [IngestionTests](../../server/tests/MediaDock.IntegrationTests/IngestionTests.cs) supplies inline RSS/XML and OMDb/JSON responses through `MockProviderHandler` and a synthetic API key; it does not call either service. |
| `Infrastructure` | Checks PostgreSQL advisory-lock coordination with separate contexts in [AdvisoryLockTests](../../server/tests/MediaDock.IntegrationTests/AdvisoryLockTests.cs). |
| `Api` | Exercises the in-process API with `WebApplicationFactory` and disposable PostgreSQL; [PersonalRatingsApiTests](../../server/tests/MediaDock.IntegrationTests/PersonalRatingsApiTests.cs) verifies owner-only list results, ascending/descending score order and stable ties, pagination, pending/not-found fallback, authorization, and owner-scoped enrichment queue deduplication/privacy. Golden Globes catalog grouping, filtering, pagination, year validation, manual-link normalization/group scope/idempotency/clearing, enrichment duplicate conflicts, and import persistence are covered in [GoldenGlobeApiTests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs) and [BackgroundJobApiTests](../../server/tests/MediaDock.IntegrationTests/BackgroundJobApiTests.cs). |
| `Background jobs` | [BackgroundJobSchedulerTests](../../server/tests/MediaDock.IntegrationTests/BackgroundJobSchedulerTests.cs) cover Sofia DST and catch-up boundaries; [BackgroundJobExecutionTests](../../server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs) executes queued award imports, Golden Globes exact-ID refresh, and owner-scoped personal IMDb enrichment through the real API-hosted dispatcher, checking exact-ID requests, shared-budget accounting, metadata persistence, owner isolation, and payload/event privacy. |

```powershell
python -B scripts/run_tests.py server-integration
python -B scripts/run_tests.py server-integration --filter "Category=Persistence"
python -B scripts/run_tests.py server-integration --filter "Category=Ingestion"
python -B scripts/run_tests.py server-integration --filter "Category=Infrastructure"
python -B scripts/run_tests.py server-integration --filter "Category=Api"
python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~PersonalRatingsApiTests"
python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~SharedImdbMigrationBackfillsNormalizedIdsAndPreservesLocalMetadata"
python -B scripts/run_tests.py server-unit --filter "FullyQualifiedName~ImdbTitleEnrichmentServiceTests"
python -B scripts/run_tests.py server-unit --filter "FullyQualifiedName~MetadataResolverTests"
python -B scripts/run_tests.py server-unit --filter "FullyQualifiedName~OmdbClientBudgetTests"
python -B scripts/run_tests.py web --filter client
python -B scripts/run_tests.py web --filter SourceSettingsView
python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~GoldenGlobe"
```

Both .NET test projects also contain an untagged `UnitTest1.Test1` placeholder; it does not represent feature coverage.

## External Services

Do not test ingestion by running a scan against live RSS feeds or OMDb. RSS transport tests inject fake HTTP and DNS handlers, and ingestion integration tests intercept both providers with local fixtures. A manual API scan is an operational request, not a test; keep verification within the isolated tests described above.

Oscar automated tests use inline CSV data, PostgreSQL Testcontainers, and stubbed OMDb responses. Final operator acceptance still requires an externally obtained Oscar dataset and the API key's confirmed daily quota; neither dataset files nor live credentials belong in the repository or routine test suite.
Golden Globes automated tests likewise use inline CSV/TSV data, disposable PostgreSQL, and stubbed OMDb responses; they do not require the external dataset or live provider credentials.

`GoogleAuthenticationApiTests.FavoritesAreScopedToCurrentActiveUserEvenWhenAccessControlIsOpen` verifies active-user enforcement in open mode, anonymous/unknown/deactivated denial, two-user isolation for the same title, administrator isolation, ignored client owner IDs, owner-scoped counts/pages/status filters, cross-user update/delete isolation, source validation, origin merging, and marker preservation using disposable PostgreSQL. `PersonalRatingsApiTests` covers owner-scoped list, sort, paging, pending/not-found fallback, authorization, enrichment enqueue deduplication/privacy, and import ownership/antiforgery behavior; `PersonalRatingsJsonParserTests` covers validation and skipped missing-rating rows. Web API-client tests cover list-query serialization and antiforgery on mutations; `SourceSettingsView.test.tsx` covers rating sort, pagination, automatic enrichment enqueue/refresh, and import compatibility. `App.test.tsx` covers account-switch isolation. `OscarApiTests.FavoritesRequireAnActiveLinkedUserEvenWhenAccessControlIsOpen` verifies anonymous favorite access remains closed. Web tests also cover anonymous suppression, favorite defaults, unavailable torrent actions, and failed marker updates with mocked API requests. Run migrations only in an isolated/staging database before the normal deployment gate; routine tests never migrate production data or contact live RSS/OMDb providers.