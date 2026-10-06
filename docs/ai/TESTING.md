# Development and Test Strategy

This guide covers testing for the standalone application in this repository. See the [AI documentation index](README.md), [architecture](ARCHITECTURE.md), and [local runbook](../../README.md).

## Prerequisites

- .NET 10 SDK for the server solution and its `net10.0` projects.
- Node.js and npm for the React/Vite project. The committed [package lock](../../web/package-lock.json) is used by `npm ci`.
- A running Docker Engine for PostgreSQL integration tests. The integration project uses Testcontainers to start disposable `postgres:17-alpine` containers; no pre-existing database or Compose stack is required. Docker must be able to use that image, pulling it if it is not already available locally.

The .NET unit tests and web tests do not require PostgreSQL, Docker, RSS sources, or OMDb credentials.

## Compact Test Runner

Run the standard-library Python runner from the repository root. It suppresses passing-test output, summarizes failures from .NET TRX and Vitest JSON reports, and keeps full logs and reports in a temporary `mediadock-tests-*` directory.

```powershell
python -B scripts/run_tests.py all
python -B scripts/run_tests.py server-unit --filter "Category=Parsing"
python -B scripts/run_tests.py server-integration --filter "Category=Api"
python -B scripts/run_tests.py web --filter CatalogView
python -B scripts/run_tests.py deploy
python -B scripts/run_tests.py runner
```

`all` runs both .NET projects, web tests, deploy-control tests, and the runner's own parser tests; integration tests still require Docker. `--filter` applies to one selected suite: it uses the VSTest filter expression for .NET, a Vitest file-path filter for web, or Python unittest's `-k` name pattern. .NET restore is skipped by default; pass `--restore` when package restore is needed. The runner exits nonzero if any selected suite fails.

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
python -B -m unittest deploy.test_deploy_control
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
dotnet test server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj --no-restore
dotnet test server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj --no-restore --filter "Category=Parsing"
dotnet test server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj --no-restore --filter "Category=RssTransport"
dotnet test server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj --no-restore --filter "Category=Matching"
dotnet test server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj --no-restore --filter "Category=GoldenGlobes"
```

## PostgreSQL Integration Tests

Run from the repository root after solution restore. These tests require Docker because they start their own PostgreSQL 17 containers; they do not use the Compose database. The [integration-test project](../../server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj) has these xUnit categories:

| Category | Coverage and test data |
| --- | --- |
| `Persistence` | Applies migrations and checks schema/occurrence uniqueness in [PersistenceTests](../../server/tests/MediaDock.IntegrationTests/PersistenceTests.cs); Golden Globes tests cover CSV/TSV import idempotency, candidate eligibility, and grouped-outcome persistence in [GoldenGlobeDatasetImporterTests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeDatasetImporterTests.cs) and [GoldenGlobeEnrichmentRepositoryTests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeEnrichmentRepositoryTests.cs). Oscar tests also verify import/enrichment persistence and the separate run-audit lifecycle in [OscarDatasetImporterTests](../../server/tests/MediaDock.IntegrationTests/OscarDatasetImporterTests.cs) and [OscarEnrichmentRepositoryTests](../../server/tests/MediaDock.IntegrationTests/OscarEnrichmentRepositoryTests.cs). |
| `Ingestion` | Checks idempotency, metadata caching, and partial failures against PostgreSQL. [IngestionTests](../../server/tests/MediaDock.IntegrationTests/IngestionTests.cs) supplies inline RSS/XML and OMDb/JSON responses through `MockProviderHandler` and a synthetic API key; it does not call either service. |
| `Infrastructure` | Checks PostgreSQL advisory-lock coordination with separate contexts in [AdvisoryLockTests](../../server/tests/MediaDock.IntegrationTests/AdvisoryLockTests.cs). |
| `Api` | Exercises the in-process API with `WebApplicationFactory` and disposable PostgreSQL; Golden Globes catalog grouping, filtering, pagination, year validation, enrichment duplicate conflicts, and import upload persistence are covered in [GoldenGlobeApiTests](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs) and [BackgroundJobApiTests](../../server/tests/MediaDock.IntegrationTests/BackgroundJobApiTests.cs). |
| `Background jobs` | [BackgroundJobSchedulerTests](../../server/tests/MediaDock.IntegrationTests/BackgroundJobSchedulerTests.cs) cover Sofia DST and catch-up boundaries; [BackgroundJobExecutionTests](../../server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs) executes queued Oscar and Golden Globes imports through the real API-hosted dispatcher and verifies payload cleanup. |

```powershell
dotnet test server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj --no-restore
dotnet test server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj --no-restore --filter "Category=Persistence"
dotnet test server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj --no-restore --filter "Category=Ingestion"
dotnet test server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj --no-restore --filter "Category=Infrastructure"
dotnet test server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj --no-restore --filter "Category=Api"
dotnet test server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~GoldenGlobe"
```

Both .NET test projects also contain an untagged `UnitTest1.Test1` placeholder; it does not represent feature coverage.

## External Services

Do not test ingestion by running a scan against live RSS feeds or OMDb. RSS transport tests inject fake HTTP and DNS handlers, and ingestion integration tests intercept both providers with local fixtures. A manual API scan is an operational request, not a test; keep verification within the isolated tests described above.

Oscar automated tests use inline CSV data, PostgreSQL Testcontainers, and stubbed OMDb responses. Final operator acceptance still requires an externally obtained Oscar dataset and the API key's confirmed daily quota; neither dataset files nor live credentials belong in the repository or routine test suite.
Golden Globes automated tests likewise use inline CSV/TSV data, disposable PostgreSQL, and stubbed OMDb responses; they do not require the external dataset or live provider credentials.

`OscarApiTests.FavoritesMergeOriginsAndKeepIndependentMarkersWhenTorrentAppears` applies additive migrations to disposable PostgreSQL and checks source validation, status filters, later torrent availability, parallel additions, manually disabled markers, Oscar lookup and idempotent deletion. The web Oscar catalog tests cover favorite defaults, unavailable torrent actions and failed marker updates with mocked API requests. Run migrations only in an isolated/staging database before the normal deployment gate; routine tests never migrate production data or contact live RSS/OMDb providers.