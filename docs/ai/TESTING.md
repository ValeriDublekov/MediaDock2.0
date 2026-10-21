# Development and Test Strategy

This guide covers testing for the standalone application in this repository. See the [AI documentation index](README.md), [architecture](ARCHITECTURE.md), and [local runbook](../../README.md).

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

## Large Diff Inspection

Use the diff inspector from the repository root instead of sending a large `git diff` through command output:

```powershell
python -B scripts/inspect_diff.py
python -B scripts/inspect_diff.py --list
python -B scripts/inspect_diff.py --file docs/ai/DATA_SCHEMA.md
python -B scripts/inspect_diff.py --file docs/ai/DATA_SCHEMA.md --start-line 1 --lines 100
python -B scripts/inspect_diff.py --file docs/ai/DATA_SCHEMA.md --file server/src/MediaDock.Api/Program.cs
```

Small diffs print normally. By default, a diff is summarized when it exceeds 300 rendered lines or any one file has more than 120 added/deleted lines. The summary lists per-file additions and deletions, and the complete snapshot is retained under the ignored `.test-tmp/` directory. `--max-lines` and `--max-file-lines` adjust those limits. `--start-line` and `--lines` print a bounded page from one selected file.

The inspector compares tracked files against `HEAD`, so it includes staged and unstaged tracked changes, but not untracked files, matching `git diff HEAD` behavior.

## EF Migration Inspection

Use the read-only helper to summarize one migration without opening historical designer snapshots:

```powershell
python -B scripts/inspect_ef_migration.py server/src/MediaDock.Infrastructure/Persistence/Migrations/20261021111712_RemoveLegacyTitleIdentity.cs
python -B scripts/inspect_ef_migration.py path/to/Migration.cs --entity ImdbFavoriteMovie --entity OscarNomination
python -B scripts/inspect_ef_migration.py path/to/Migration.cs --format json --output migration-report.json
python -B scripts/inspect_ef_migration.py path/to/Migration.cs --full-sql
```

The adjacent `MediaDockDbContextModelSnapshot.cs` is used by default; pass `--snapshot PATH` to select another snapshot. Entity selection prints only its table mapping, properties/columns, indexes, and relationships. SQL operation count and a bounded excerpt are always shown; `--full-sql` includes complete SQL. Reports written with `--output` are restricted to `.test-tmp/`. Snapshot comparisons flag drops that still appear in the target model as suspicious review signals, not proof that a migration is incorrect.

## File Line Inventory

Use the inventory to identify large text files for review; line count alone is not a reason to refactor. Results are sorted by descending count. Binary files and known generated, dependency, cache, and test-artifact directories are skipped.

```powershell
python -B scripts/report_file_lines.py --min-lines 300
python -B scripts/report_file_lines.py
```

`--min-lines 300` shows files with at least 300 lines. Omitting the option lists every eligible text file.

## Code Complexity Analysis

Lizard reports per-function cyclomatic and cognitive complexity. Install the pinned development dependency into the project virtual environment, then use the helper to print only the highest-scoring functions:

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
\.venv\Scripts\python.exe -B scripts\analyze_complexity.py --top 10
\.venv\Scripts\python.exe -B scripts\analyze_complexity.py --top 10 server/tests
\.venv\Scripts\python.exe -B scripts\analyze_complexity.py --top 5 server\src\MediaDock.Application\Metadata\MetadataResolver.cs
```

With no path, the helper analyzes `server/src` and `web/src`; a file or directory path narrows the scan. The `--top` default is 10. Build output and generated EF migration files are excluded. Treat high scores as review signals, not automatic refactoring requirements, and consider them together with function length, parameters, cohesion, and test coverage.

## Documentation Links

After adding or moving Markdown links, run the local checker from the repository root:

```powershell
python -B scripts/check_markdown_links.py
```

It checks local inline links and image targets, including Markdown heading fragments and explicit HTML anchors. External URLs are skipped; the check does not make network requests. Use `scripts/report_file_lines.py` above when a file-line inventory is needed.

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