# Golden Globes IMDb Import Procedure

Use this procedure whenever a Golden Globes IMDb event-page link is provided. The default is an existing-awards-only, reviewable SQL import; a link alone is enough to start without asking again about category scope.

## Default Scope

- Read the current award names from `golden_globe_awards` through an approved read-only connection. Do not rely on an old count or category list.
- Include every rendered IMDb category with nominees that maps to an existing database award. Keep the IMDb label and exact database award name separately; record any punctuation alias explicitly.
- Do not create award rows for unmatched IMDb categories by default. List their nominees and IMDb IDs in the audit, then exclude them from SQL.
- If the user explicitly requests a specific unmatched category, add only that award with an idempotent `INSERT ... ON CONFLICT` before the SQL preflight and record the approval in the audit. Keep other unmatched categories excluded.
- A category in “Jump to” with no rendered section has no nomination rows to import. A database award with no rendered section also contributes no rows.
- If the user explicitly requests a full-page import, stop before generating SQL when categories need new database awards; explain the additional scope and wait for approval to add them.

The 2026 check is the example: 30 categories in “Jump to”, 28 rendered award sections, and two selector-only entries (`Cecil B. DeMille Award`, `Carol Burnett Award`). The initial existing-awards-only SQL covered 9 categories. At the user's explicit request, the current SQL adds `Best Screenplay - Motion Picture` and its six film-title nominations, for 10 categories, 62 nominations, and 49 title groups; the other 18 rendered categories without listed database awards remain excluded. The prior database inventory claimed 11 names but enumerated 10; `Best Performance in Stand-Up Comedy on Television` was present in the original matched set.

The 9 pre-existing database award names originally matched for that 2026 snapshot were:

- Best Motion Picture - Drama
- Best Motion Picture - Musical or Comedy
- Best Motion Picture - Animated
- Best Motion Picture – Non-English Language
- Cinematic and Box Office Achievement
- Best Television Series - Drama
- Best Television Series - Musical or Comedy
- Best Television Limited Series, Anthology Series, or Motion Picture Made for Television
- Best Performance in Stand-Up Comedy on Television

`Best Screenplay - Motion Picture` was not in that supplied list and is now added only in the explicitly requested 2026 SQL. This is an example, not a static allowlist. Always re-query the current names and reconcile counts instead of copying the prior inventory.

## Procedure

1. Confirm the ceremony year from both the event URL and page heading. Reopen the supplied page.
2. Scroll from the top through overlapping increments to the bottom. Wait for content after each pass; at the bottom, wait until the document height and bottom position remain stable for at least three checks. Disable smooth scrolling if it prevents a real position change. Record the final height, wait, and bottom position. Do not claim all lazy-loaded content was verified unless this check was completed.
3. Enumerate every “Jump to” option, every rendered award-section heading, and every title link in each rendered section. Record source category, displayed title, exact `/title/tt...` IMDb ID, winner status, and rendered-link count. Compare selector entries to rendered headings in both directions. List selector-only entries and rendered sections absent from the selector.
4. Query and save the current database names with a read-only statement:

   ```sql
   SELECT name FROM golden_globe_awards ORDER BY name;
   ```

   Match categories exactly where possible. For punctuation or spelling differences, use a documented explicit mapping, never a fuzzy guess. Example: IMDb `Best Motion Picture - Non-English Language` maps to database `Best Motion Picture – Non-English Language` (en dash). Keep the page category for the audit; use the database name in the nomination row and import key.
5. Use `movie` for motion-picture groups and `series` for television groups, consistent with the importer and database constraint. The schema has no other nominee type. Flag an ambiguous special or unsupported media type rather than inventing one.
   The separate compact-CSV path in [`prepare-golden-globes.py`](../../scripts/prepare-golden-globes.py) accepts only title nominees (`film`/`movie`/`tv-show`/`series`). The provided historical CSV stores screenplay nominees as `people`, so those rows are intentionally excluded; do not relabel screenwriters as film titles. The helper reports excluded categories by nominee type. For title-level screenplay nominees, use the IMDb event page and the SQL procedure above.
6. Build one data row per import identity, not per actor/person card. The database groups titles by `(title, year, nominee_type)`; nominations are keyed by ceremony year, winner, database award name, and title. If repeated cards for the same title/category would produce the same key, report the raw card count and the deduplicated row count. If the same title/category has conflicting winner states, stop and flag it rather than silently collapsing it.
7. Generate the source-audit Markdown and the SQL review artifact at `.test-tmp/golden-globes-<year>-source-audit.md` and `.test-tmp/golden-globes-<year>.sql`. Use [the SQL template](templates/golden_globes_import.sql). The audit must include source URL, capture evidence, selector/section comparison, category-to-database mapping, included and excluded categories, per-category title/ID list, winner and nomination counts, and limitations.
8. Validate before delivery: all rendered categories accounted for; all included IDs match `^tt[0-9]{7,10}$`; source rows match independently recorded category, winner, and title-group counts; every generated import key matches the importer algorithm; every included award exists; no existing title group has a conflicting IMDb link. Smoke-test the complete generated SQL only against a disposable local PostgreSQL database using a schema that matches the relevant constraints.
9. Return both artifacts and a concise count/omission summary. The SQL is for the user to review and execute later.

## Import Identity And Safety

The authoritative behavior is in [`GoldenGlobeDatasetImporter`](../../server/src/MediaDock.Infrastructure/GoldenGlobes/GoldenGlobeDatasetImporter.cs), the [Golden Globes schema](DATA_SCHEMA.md), and [persistence behavior](DATA_BEHAVIOR.md).

- A title group is unique by `(title, year, nominee_type)`. The year is the ceremony year, not the title's release year.
- The import key is lowercase hexadecimal SHA-256 of UTF-8 bytes for `v1:{year}\u001f{Winner.ToString()}\u001f{award_name}\u001f{title}`. `Winner.ToString()` is .NET `True` or `False`; each `\u001f` is the U+001F unit separator. `award_name` is the exact database award name, including punctuation. Preserve source title spelling and Unicode.
- The API stores provider metadata only in `imdb_titles`. The SQL may create a pending `imdb_titles` row when absent, pin the source IMDb ID on the Golden Globes title group, and insert nominations. It must preserve an existing enriched `imdb_titles` record and must fail closed on a conflicting existing group link.
- The SQL template is intentionally incomplete, creates no award rows by default, and contains no `background_jobs` inserts. For an explicitly approved new category, add its idempotent award insert to the review SQL before preflight. A template run without populated input tables raises in preflight.
- Manual IMDb links are never re-matched by title. The ordinary Golden Globes batch enrichment refreshes pinned groups by exact IMDb ID when their enrichment is pending, a temporary error is due, or the group is marked enriched while its canonical `imdb_titles` metadata is not enriched. After reviewing and applying the SQL, run the Golden Globes enrichment job. For a single group, the admin-only `PUT /api/golden-globes/imdb-link` endpoint still queues an exact-ID refresh; its request uses `filmId` formatted as `<year>:<movie|series>:<title>` and `imdbId`. Poll the returned refresh job URL. See [`GoldenGlobeApiTests`](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs) and [background jobs](API_OPERATIONS_AND_JOBS.md). Never insert refresh jobs directly.

## Authoritative References

- IMDb category and title IDs: the supplied event page, after the bottom-scroll verification above.
- Current award names: a fresh read-only query against `golden_globe_awards`.
- Import rules and identity: [`GoldenGlobeDatasetImporter`](../../server/src/MediaDock.Infrastructure/GoldenGlobes/GoldenGlobeDatasetImporter.cs).
- Manual IMDb refresh route: [`GoldenGlobeApiService`](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeApiService.cs), [`GoldenGlobeEndpoints`](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeEndpoints.cs), and [`GoldenGlobeApiTests`](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs).