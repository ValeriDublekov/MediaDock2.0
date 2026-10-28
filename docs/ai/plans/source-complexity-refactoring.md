# Source Complexity Refactoring Backlog

## Purpose

This is a candidate backlog from the static complexity and file-line scans run on 2026-10-28. These are review signals, not approved delivery commitments. Re-run both scans and inspect the current source before scheduling work.

## Priority Candidates

1. **Recommendation API orchestration** - [`MovieRecommendationsApiService.cs`](../../../server/src/MediaDock.Api/Recommendations/MovieRecommendationsApiService.cs), `GetAsync`: CogC 34, CCN 50, NLOC 265. Review boundaries between request validation, input loading, award data assembly, scoring, and response mapping. Preserve transaction isolation, deterministic ordering, paging, and response behavior. This file had uncommitted changes during the assessment, so re-check its current shape before implementation.
2. **Golden Globes dataset import** - [`GoldenGlobeDatasetImporter.cs`](../../../server/src/MediaDock.Infrastructure/GoldenGlobes/GoldenGlobeDatasetImporter.cs), `ImportAsync`: CogC 50, CCN 28, NLOC 128. Consider separating input parsing/validation from nomination reconciliation and persistence. Preserve idempotency, category aliases, linked-title handling, transaction boundaries, and batched saves.
3. **Background ingestion UI** - [`BackgroundIngestionPanel.tsx`](../../../web/src/features/sources/BackgroundIngestionPanel.tsx): CogC 51, CCN 44, NLOC 121 for the component; 393 lines in the file. Review whether polling/job-dialog behavior and job submission/upload flows can be separated without duplicating status state or request handling.
4. **Personal ratings UI** - [`ConfigurationSettingsViews.tsx`](../../../web/src/features/sources/ConfigurationSettingsViews.tsx), `PersonalRatingsView`: CogC 42, CCN 46, NLOC 139; 514 lines in the file. Review extracting the ratings view from the settings module and keeping import, enrichment, sorting, and deletion behavior independently understandable.

## File-Size Review Only

These files are large but were not among the highest-complexity functions in the scan. Inspect cohesion and responsibility boundaries before proposing any split; line count alone is not sufficient.

- [`BackgroundJobApiService.cs`](../../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobApiService.cs): 603 lines; serves multiple job status and enqueue workflows.
- [`PostgresRssIngestionRepository.cs`](../../../server/src/MediaDock.Infrastructure/Ingestion/PostgresRssIngestionRepository.cs): 620 lines; inspect whether its persistence operations form clear cohesive groups.
- [`OmdbClient.cs`](../../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs): 545 lines; review the client responsibilities and existing test boundaries.
- [`client.ts`](../../../web/src/api/client.ts): 542 lines; review API grouping and shared request/error handling before considering module boundaries.

## Do Not Treat As Findings

- Lizard reported `readSavedImdbRatingRanges` in [`CatalogView.tsx`](../../../web/src/features/catalog/CatalogView.tsx) at CogC 66 and NLOC 178, although the function body is short. Treat this as a likely TypeScript/TSX parser attribution issue unless independently confirmed.
- [`deploy/systemd/README.md`](../../../deploy/systemd/README.md) is 335 lines, only slightly above the inventory's 300-line review threshold. No split is indicated without a navigation or duplication problem.
- Generated EF migration snapshots/designers and `web/package-lock.json` are not manual refactoring targets.

## Exit Criteria

- Each implementation starts from a fresh scan and a focused review of the affected code and tests.
- Extracted responsibilities have explicit inputs/outputs and do not change API contracts, persistence behavior, or user-visible behavior.
- Existing regression coverage is retained or extended; run the narrowest applicable repository suite as described in the [testing guide](../TESTING.md).
- Re-run the focused complexity scan after each coherent change and verify that the result reflects the intended function boundaries.