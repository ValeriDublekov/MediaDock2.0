# All IMDb Titles Catalog: Phase 1 API

## Goal

Provide a canonical, paged IMDb list API and type-agnostic personal-state writes within the confirmed enrichment rules. Administrator add/forced-refresh operations are deferred to the follow-up session recorded in the [master plan](all-imdb-titles-catalog.md#session-scope).

## Dependencies

- Follow the [master plan](all-imdb-titles-catalog.md) decisions and read [API contracts](../API_CONTRACTS.md), [catalogs and awards](../API_CATALOGS_AND_AWARDS.md), [identity and personal data](../API_IDENTITY_AND_PERSONAL_DATA.md), [operations and jobs](../API_OPERATIONS_AND_JOBS.md), [data contracts](../DATA_CONTRACTS.md), [data schema](../DATA_SCHEMA.md), and [data behavior](../DATA_BEHAVIOR.md).
- Preserve existing owner and administrator authorization policies. Inspect current working-tree changes before editing.

## Tasks

1. Add a dedicated paged `GET /api/imdb-titles` over `ImdbTitle`, endpoint/service/DTO registration, and OpenAPI metadata. Include all canonical rows, including null/unknown media types and all metadata statuses. Use a normalized page/page-size contract and stable tie-breakers.
2. Add validated query support for title/IMDb ID search, media type, genre(s), year and IMDb-rating ranges, metadata status, page, page size, and sort. Default to year descending then title ascending. Support rating, votes, and year sorts; null numeric values sort last, then title and IMDb ID break ties.
3. Return fields needed for poster/table rows and details, including IMDb ID, title, year, nullable open-ended media type, rating, votes, genres, poster, metadata status, and awards. Return year/rating bounds and filter options as needed by the UI.
4. Optionally resolve the active user on list reads and project only that user's favorite/To watch/To download flags, ignored state, and personal rating. Server-side ignored and rated visibility filters must affect both items and page totals.
5. Update favorite, ignored, and manual personal-rating writes to accept any canonical IMDb media type. Preserve the confirmed rules: favorite and rating require `metadata_status = enriched`; ignore requires an existing canonical row. Preserve owner checks, duplicate behavior, and unrelated origin/source rules. Do not change recommendation eligibility or marking-watched restrictions.
6. Ensure personal response DTOs preserve nullable arbitrary media types rather than falling back to `movie` or using a closed movie/series union. No title or personal-state table migration is expected for this slice.
7. Deferred: add separate administrator-authorized title candidate search/lookup/add operations for the shared catalog. Preserve the existing owner-scoped manual-list endpoints; catalog add must persist metadata only.
8. Deferred: add an administrator-authorized targeted refresh job with cache bypass, shared-budget use, per-ID active-job deduplication, returned-ID validation, non-destructive metadata merge, and job status/results. Scaffold and document its required job-type/index migration in that follow-up.
9. Add/update API and data documentation for the list contract and all-type personal writes. Admin mutation and forced-refresh documentation is deferred with those operations.

## Tests

- Add `ImdbTitlesApiTests` for non-torrent rows, all types/null types/statuses, search/filter bounds, all sort orders, null sorting, stable pagination, and ignored/rated toggles.
- Verify owner isolation for favorite/ignored/rating projections and mutations. Test all media types for writes, while asserting favorite/rating still reject non-enriched rows and ignore accepts existing rows at any metadata status.
- Verify existing owner-scoped manual-list routes retain their policies.
- Deferred: test admin candidate/add authorization and metadata-only persistence.
- Deferred: test forced-refresh cache bypass, provider outcomes, job deduplication/status, and preservation of stored fields.
- Keep `MovieRecommendationsApiTests` proving recommendations and mark-watched remain movie/series-only.

## Verification

Run the narrow tests through the repository runner, for example:

```powershell
python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~ImdbTitlesApiTests"
python -B scripts/run_tests.py server-integration --filter "Category=Api"
python -B scripts/run_tests.py server-unit --filter MetadataResolver
```

Integration tests require Docker. Check Docker availability and run the relevant suites; if Docker is unavailable, report it as a blocker rather than skipping integration coverage. Run the server solution build after the API slice is integrated.

## Exit Criteria

- The API returns all canonical title types and statuses with correct filtering, ordering, paging, and only the active user's personal state.
- Type-agnostic favorite/ignore/rating behavior matches the confirmed metadata rules, with recommendation/source-specific constraints intact.
- Administrator add and forced refresh, including job migration and success/failure coverage, remain deferred to the follow-up session.
- API/data documentation and focused tests pass; no unrelated migration or schema change is introduced.