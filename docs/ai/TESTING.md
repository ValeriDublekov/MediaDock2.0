# Development and Test Strategy

This guide covers testing for the standalone application in this repository. See the [AI documentation index](README.md), [development tools](DEVELOPMENT_TOOLS.md), [architecture](ARCHITECTURE.md), and [local runbook](../../README.md).

## Prerequisites

Use the repository's required runner for every test: `python -B scripts/run_tests.py <suite>`. Direct `dotnet test`, Vitest, or `unittest` invocation bypasses the repository's failure-summary and artifact handling.

- .NET 10 SDK for the server solution and its `net10.0` projects.
- Node.js and npm for the React/Vite project; `npm ci` uses the committed [package lock](../../web/package-lock.json).
- Docker Engine for integration tests. Testcontainers starts disposable PostgreSQL 17 containers; no existing database or Compose stack is needed.

Unit, web, deploy-control, and runner tests need no Docker, PostgreSQL, RSS sources, or OMDb credentials.

## Compact Test Runner

Run from the repository root. The runner supports `server-unit`, `server-integration`, `web`, `deploy`, `runner`, and `all` (`all` runs each suite). .NET restore is skipped unless `--restore` is supplied.

```powershell
python -B scripts/run_tests.py all
python -B scripts/run_tests.py server-unit --filter "Category=Parsing"
python -B scripts/run_tests.py server-integration --filter "Category=Api"
python -B scripts/run_tests.py web --filter CatalogView
python -B scripts/run_tests.py deploy
python -B scripts/run_tests.py runner
```

`--filter` applies to one suite: VSTest expression for .NET, a Vitest file-path filter for web, or Python unittest's `-k` pattern for `deploy` and `runner`. Each run captures logs in a unique `.test-tmp/mediadock-tests-*` directory; .NET and web runs also write structured reports. Use `--artifacts-dir <path>` to select another base directory. The runner prints bounded failure summaries, retains full artifacts, and exits nonzero if any selected suite fails.

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
npm run build
```

The [web package scripts](../../web/package.json) define `lint` as Oxlint and `build` as TypeScript project build followed by Vite build. Run web tests through the standard runner.

The host deployment-control bridge uses only Python's standard library and can
be checked without systemd or Docker:

```powershell
python -B scripts/run_tests.py deploy
```

## .NET Unit Tests

The [unit-test project](../../server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj) uses xUnit. Current categories are `Parsing`, `RssTransport`, `Matching`, and `GoldenGlobes`; other tests can be selected by fully qualified test name.

## PostgreSQL Integration Tests

The [integration-test project](../../server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj) uses xUnit categories `Persistence`, `Ingestion`, `Infrastructure`, and `Api`. These tests require Docker and use disposable PostgreSQL containers rather than the Compose database.

```powershell
python -B scripts/run_tests.py server-unit --filter "Category=Parsing"
python -B scripts/run_tests.py server-integration --filter "Category=Api"
python -B scripts/run_tests.py web --filter CatalogView
python -B scripts/run_tests.py deploy
python -B scripts/run_tests.py runner
```

## External Services

Do not test ingestion with live RSS feeds or OMDb. Automated tests use local fixtures, stubbed provider responses, and disposable databases; a manual API scan is an operational action, not a test.
