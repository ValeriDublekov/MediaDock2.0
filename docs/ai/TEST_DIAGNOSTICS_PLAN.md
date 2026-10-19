# Test Diagnostics and File Inventory Plan

## Goal

Reduce wasted time and context from test artifacts and noisy diagnostics. Keep the existing test runner and structured report parsers, make artifacts workspace-local by default, suppress verbose EF Core command logs in integration tests, and add a repeatable file line-count inventory. Use the inventory to recommend focused refactoring; do not refactor application code automatically.

## Current Behavior

- `scripts/run_tests.py` captures each child process's stdout and stderr into a per-suite log, parses .NET TRX and Vitest JSON reports, and prints a bounded failure summary (up to 12 failures and 16 lines per failure).
- Raw test output is not streamed to the normal terminal. EF Core SQL can still inflate stored logs and possibly captured test output. If a structured report is missing, the current fallback may print an arbitrary tail of the raw log, which can expose noisy SQL in the terminal.
- Test artifacts currently go under the operating-system temp directory. This can trigger access approvals when inspecting them outside the workspace.
- There is no project-wide line-count inventory script. Initial size estimates were found by code search and are approximate; the new script's output will be authoritative.

## Decisions

- Keep the existing runner and TRX/Vitest parsers; do not add a second general-purpose test-log parser.
- Default artifacts to an ignored workspace directory, `.test-tmp/`, and keep a unique subdirectory for each run. Preserve artifacts for diagnosis; do not delete older runs automatically. `--artifacts-dir` selects a different base directory.
- Apply `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning` only to the `server-integration` child process. This should filter EF command-level Information logs before they reach logging providers while retaining Warning/Error. It must not alter production configuration.
- Improve the missing-report fallback to print recognized, bounded build/test diagnostics. If it cannot identify useful diagnostics, print the exit status and artifact path instead of dumping arbitrary raw log lines.
- Count text files across the project, including documentation, configuration, data, generated source, and lockfiles. Skip binary files and generated build, dependency, cache, and test-artifact directories. Line count is an inventory signal, not evidence of a runtime bottleneck.
- Do not add dedicated build or Docker output parsers in this iteration. Prefer the commands' native quiet/limited-output options (for example, .NET verbosity and Docker Compose progress/log-tail options). Consider a focused wrapper only if repeated real workflows still produce excessive output.

## Work Plan

### 1. Test runner and log noise

1. Add `--artifacts-dir` to `scripts/run_tests.py`. Default to `.test-tmp/` under the repository root, create a unique child directory for every run, and continue to print the exact artifact path.
2. Add `.test-tmp/` to the root `.gitignore`.
3. Pass the EF logging-level override only to the integration-test child process. Do not change API production appsettings or global logging defaults.
4. Replace the arbitrary log-tail fallback with bounded diagnostics selected from known compiler/test error patterns. Keep the existing structured summary and output limits.
5. Extend `scripts/test_run_tests.py` to cover artifact directory behavior, child-process environment scoping, diagnostic bounds, and the missing-report fallback.

### 2. Project line-count inventory

1. Add `scripts/report_file_lines.py`, deriving the project root from the script location.
2. Print relative paths and exact line counts in deterministic descending-count order. Support `--min-lines N` to show only files at or above a threshold such as 300. With no threshold, list every eligible text file.
3. Stream files so large datasets do not need to be loaded into memory. Handle files without a final newline consistently; skip binary files and known generated/dependency/cache/artifact directories.
4. Add focused tests for line counting, thresholds, sorting, exclusions, empty files, and files without a trailing newline. Include these in the existing `runner` suite.

### 3. Refactoring assessment

Run `python -B scripts/report_file_lines.py --min-lines 300`, then inspect the largest source and test files for independent responsibilities, cohesion, and existing test boundaries. Return prioritized recommendations only; any actual source refactoring is a separate decision.

Preliminary candidates from the initial approximate scan, to confirm with the script and targeted review:

- `server/src/MediaDock.Application/Ingestion/RssIngestionService.cs` (about 965 lines): orchestration, per-entry processing, feed parsing, redaction, progress, and summary logic share one class.
- `server/src/MediaDock.Api/BackgroundJobs/BackgroundJobDispatcher.cs` (about 587 lines): dispatcher lifecycle/recovery/claim logic sits alongside several job-specific handlers.
- `server/tests/MediaDock.IntegrationTests/GoogleAuthenticationApiTests.cs` (about 1,230 lines): favorites, sign-in/registration/linking, administration, and test helpers share one file.
- `server/tests/MediaDock.IntegrationTests/IngestionTests.cs` (about 797 lines): multiple scenarios share a large mock provider and fixtures.
- `server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs` (about 547 lines): several distinct background-job workflows are covered in one file.
- `web/src/features/sources/BackgroundIngestionPanel.tsx` (about 393 lines): polling/events and multiple enqueue/import workflows share one component.

These line counts are provisional. Do not nominate API contracts, shared styles, migration history, datasets, lockfiles, or deployment scripts for refactoring based on size alone.

### 4. Documentation

Update `docs/ai/TESTING.md` with the artifact location and option, the integration-only EF logging filter, concise failure behavior, and the inventory command.

## EF Console Logging: Expected Behavior and Check

The normal runner does not stream the test process's raw output to the terminal; its stdout/stderr are captured in the per-suite `.log`, and the terminal receives the runner summary. Therefore, filtering EF SQL is primarily intended to keep `.log` and any test-captured output small, not to silence a normal live console stream. The improved fallback also prevents raw SQL tails from appearing in the terminal when a report is absent.

Verify the environment override with a focused integration run. Confirm that EF command SQL at Information is absent from the generated `.log` and relevant TRX/test output, while EF Warning/Error events remain. Include coverage for the custom logger provider used by `GoogleAuthenticationApiTests`, whose logger itself reports all levels as enabled. If the standard environment filter does not control that captured provider, adjust the integration test logging setup without changing production logging.

## Verification

Run repository tests only through `python -B scripts/run_tests.py <suite>`.

1. Run `python -B scripts/run_tests.py runner --filter artifact` for focused runner changes, then `python -B scripts/run_tests.py runner` for the runner suite.
2. Run `python -B scripts/report_file_lines.py --min-lines 300` and verify counts, ordering, exclusions, and the preliminary candidates. Run without `--min-lines` to inspect the complete inventory.
3. Check Docker availability and run `python -B scripts/run_tests.py server-integration --filter "Category=Api"`. Inspect the generated `.log` and `.trx` for absent EF command SQL and retained Warning/Error diagnostics. If Docker is unavailable, report it as a blocker rather than skipping the relevant integration suite.
4. Do not run build or Docker output-parser tests unless a dedicated wrapper is later justified. No full `all` suite is required for this bounded tooling change; broaden verification if implementation touches shared behavior beyond the runner and inventory script.