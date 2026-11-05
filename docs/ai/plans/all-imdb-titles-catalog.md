# All IMDb Titles Catalog

## Goal

Add a browseable, paged catalog of every canonical row in `imdb_titles`. The screen combines torrent-catalog filtering with the Golden Globes catalog's poster/table presentation, exposes the current user's personal states, and provides administrator-only metadata add and forced OMDb refresh actions.

## Planning Layout

| Phase | Scope | Detailed plan |
| --- | --- | --- |
| 1 | Canonical IMDb API, personal states, and admin metadata operations | [Phase 1](all-imdb-titles-phase-1-api.md) |
| 2 | Web catalog, interactions, documentation, and final verification | [Phase 2](all-imdb-titles-phase-2-web.md) |

Phase 1 establishes the list, ownership, authorization, add, and refresh contracts. Phase 2 consumes those contracts and completes the cross-layer checks. No schema migration is expected based on the current state-table constraints.

## Session Scope

The current slice delivers the canonical list API, server-backed filters and personal-state projection, type-agnostic personal writes, and the browse/details UI. Administrator candidate search/add and forced OMDb refresh are deferred to a follow-up session; no refresh-job type or migration is part of this slice. Existing owner-scoped `/manual-*` routes remain unchanged.

## User-Confirmed Behavior

- Include every row in `imdb_titles`, including all media types, null/unknown types, and pending or failed metadata statuses. The catalog is for all users, not only administrators.
- Show ignored and watched/rated titles by default. Provide separate controls to hide/show each. Display favorite, ignored, and rated state for the active user only.
- Watched means a personal rating exists for the active user. Any IMDb type can be favorited, ignored, or rated, but favorite and rating actions require enriched metadata. Ignoring requires an existing canonical IMDb row, not a particular media type or enrichment status.
- Adding a title by IMDb ID or title creates/updates only shared IMDb metadata. It does not create a personal favorite, ignored row, or rating. Adding and forced OMDb refresh are administrator-only.
- Forced refresh must bypass cached exact-ID successes and misses, use the shared OMDb request budget, verify that OMDb returned the requested IMDb ID, and preserve existing fields when the provider omits them.
- The default order is year descending, then title ascending. Other choices are IMDb rating, IMDb vote count (popularity), and year, all descending. Unknown numeric values sort last; title and IMDb ID provide deterministic tie-breakers. The data model stores year, not a full release date, so newest means newest year.

## Domain Constraints

- `imdb_titles` remains the canonical shared metadata owner; source-owned Oscar, Golden Globes, and torrent rows remain linked by IMDb ID.
- Personal state is always scoped to the active linked user. Never accept a client-supplied user ID or expose one user's favorite, ignored, or rating state to another user.
- Keep recommendation eligibility and watched actions movie/series-only. Preserve the existing Oscar, torrent, Golden Globes, and source-specific matching rules.
- Do not use torrent-row IDs or torrent-only `TitleDetailsDialog` for canonical IMDb rows.
- Do not broaden network bindings or alter authentication/deployment boundaries. Follow the [security guide](../SECURITY.md) and [repository instructions](../../../.github/instructions/mediadock.instructions.md).

## Architecture Decisions

1. Add a dedicated paged `GET /api/imdb-titles` over `MediaDockDbContext.ImdbTitles`; do not expand `CatalogApiService`, which reads torrent-backed rows. Use the existing page response convention and stable server-side ordering.
2. Resolve the active user optionally on catalog reads and project only that user's favorite flags, ignored state, and personal rating. Shared title metadata remains independent of personal state.
3. Keep the existing owner-scoped `/api/imdb-titles/manual-*` workflow intact for personal lists. Add separate administrator-authorized candidate/add operations for the shared catalog. A catalog favorite uses the existing non-torrent "To watch" convention, not the torrent "To download" marker.
4. Implement a targeted admin refresh as a background job using the existing job-status pattern. Reuse provider validation and the shared request budget; do not treat the OMDb settings diagnostic or ordinary cached lookup as a forced refresh.
5. Keep `mediaType` nullable and open-ended in canonical IMDb DTOs. Do not widen unrelated torrent/source or recommendation types.
6. Use server-side filters for ignored and watched/rated visibility so totals and pagination reflect the chosen view. Compute year/rating bounds from the canonical dataset, excluding null values.
7. Keep OMDb refresh merge semantics explicit: an absent/empty provider field must not erase a stored value. Report job failure, not-found, ID mismatch, and quota outcomes without changing canonical identity.

## Acceptance Criteria

- The list includes non-torrent, non-movie/series, null-type, pending, enriched, not-found, and retryable canonical rows without dropping them because of an unsupported type.
- Search, type/genre filters, year/rating ranges, metadata status, sorting, pagination, and ignored/watched visibility are server-backed and deterministic.
- Favorite/ignored/rated state is isolated to the active user; all three actions work for every type subject to the confirmed metadata-status rules.
- Catalog add and forced refresh are administrator-only; add persists metadata only, and refresh bypasses cache while respecting shared provider limits.
- The web catalog offers poster and table views, shows all three personal states, and presents linked awards compactly on one line with fuller details available from the title dialog.
- Existing recommendation, Oscar, torrent, and personal-list behavior remains covered and unchanged except for the agreed all-type personal-state writes.
- API contracts, personal-data rules, and background-job behavior are documented, and all phase exit criteria pass.

## Relevant References

- [Architecture](../ARCHITECTURE.md), [API contracts](../API_CONTRACTS.md), [catalogs and awards](../API_CATALOGS_AND_AWARDS.md), [identity and personal data](../API_IDENTITY_AND_PERSONAL_DATA.md), [operations and jobs](../API_OPERATIONS_AND_JOBS.md).
- [Data contracts](../DATA_CONTRACTS.md), [data schema](../DATA_SCHEMA.md), [data behavior](../DATA_BEHAVIOR.md), and [testing guide](../TESTING.md).
- UI patterns: `web/src/features/catalog/CatalogView.tsx`, `web/src/features/golden-globes/GoldenGlobeCatalogView.tsx`, `web/src/components/MoviePresentation.tsx`, `web/src/components/ImdbCandidatePicker.tsx`, and the favorite/ignored controls.

## Handoff

For the follow-up session, resume Phase 1 tasks 7-8 (admin candidate/add and targeted forced refresh, including its job migration) and Phase 2 admin UI/polling items. Before editing, inspect `git status --short`, reread the linked task-specific API/data docs, and verify current endpoint/service/test patterns. Preserve unrelated worktree changes. Use the repository test runner for every test; do not run `dotnet test`, Vitest, or unittest directly. Integration tests require Docker and must not be skipped when relevant.