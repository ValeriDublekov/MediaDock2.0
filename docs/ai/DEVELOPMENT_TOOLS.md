# Development Tools

This guide covers repository inspection helpers that complement the [testing guide](TESTING.md). See the [AI documentation index](README.md) for other task-specific references.

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

It checks local inline links and image targets, including Markdown heading fragments and explicit HTML anchors. External URLs are skipped; the check does not make network requests.