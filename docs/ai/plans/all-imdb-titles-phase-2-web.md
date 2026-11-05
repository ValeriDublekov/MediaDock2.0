# All IMDb Titles Catalog: Phase 2 Web

## Goal

Deliver the all-title browse experience and complete cross-layer documentation and verification using Phase 1's list and personal-state contracts. Administrator add/refresh interactions are deferred as recorded in the [master plan](all-imdb-titles-catalog.md#session-scope).

## Dependencies

- Complete [Phase 1](all-imdb-titles-phase-1-api.md) and follow the [master plan](all-imdb-titles-catalog.md).
- Follow [frontend architecture](../ARCHITECTURE.md), [testing](../TESTING.md), and the existing catalog/personal-data API contracts. Inspect the worktree and preserve unrelated changes.

## Tasks

1. Add typed query/response contracts and client functions for the all-title list. Represent canonical media type as nullable/open-ended; keep torrent/source and recommendation unions narrow.
2. Add an "IMDb titles" route available to regular catalog users. Require an active linked user for personal actions. Do not expose a client-side user ID. Admin controls are deferred.
3. Build a dedicated catalog view using the existing poster/table, view-mode, pagination, and filter patterns. Keep layout and language consistent with the Golden Globes catalog, but use canonical IMDb details rather than torrent-row details.
4. Add title/IMDb ID/year search, genre and media-type filters, year and IMDb-rating range controls, metadata-status filter, sort selector, and clear action. Default sort is year descending then title ascending; optional rating, vote count, and year sorts are descending with null values last.
5. Add separate "Show ignored" and "Show watched/rated" controls, both enabled by default. Show favorite, ignored, and personal-rating indicators. Ignore any existing title regardless of type/status; allow favorite/rating for all types only after metadata is enriched. Watched means an existing current-user personal rating; use the standard personal-rating API rather than recommendation-only watched actions.
6. Use the existing non-torrent To watch favorite behavior for this catalog.
7. Add an IMDb-identity details dialog suitable for any media type. Do not use `TitleDetailsDialog`, which requires a torrent ID. Admin add and refresh/polling UI are deferred.
8. Render linked Oscar/Golden Globes information as a compact single-line summary in rows/cards; show fuller award details in the title dialog. Use a tooltip or accessible details view for text shortened in the row.
9. Add web tests for route/access, query serialization, sort/filter behavior, null/unknown types, personal-state badges/actions, rating eligibility, and compact awards. Update relevant favorite/ignored/personal-rating shared-control tests without changing recommendation behavior. Admin add/refresh tests are deferred.
10. Update the AI documentation index and API/job docs. Run the required server/web checks and inspect the rendered page at desktop and mobile widths. When implementation needs a dev server, start it after the changes and provide its URL at handoff.

## Tests

- Add `web/src/features/imdb-titles/ImdbTitlesCatalogView.test.tsx` and cover route/client behavior in `App.test.tsx` and `api/client.test.ts`.
- Extend relevant favorite, ignored, and rating tests for arbitrary media types and nullable type values. Keep Oscar/recommendation-specific expectations unchanged.
- Verify title/year and range boundaries, null sort placement, page reset on filter/sort changes, both visibility toggles, all three state indicators, and loading/error/empty states.
- Deferred: verify queued refresh polling stops on terminal status, reloads the updated title, and displays provider failure outcomes.

## Verification

Use the repository runner for tests:

```powershell
python -B scripts/run_tests.py web --filter ImdbTitlesCatalogView
python -B scripts/run_tests.py web --filter App.test.tsx
python -B scripts/run_tests.py web --filter client.test.ts
```

Also run focused shared-action tests, then all affected API integration tests from Phase 1. Check Docker availability and do not skip relevant integration tests. Run `dotnet build server/MediaDock.sln --no-restore` and, from `web`, `npm run lint` and `npm run build`. For the documentation edits, run `python -B scripts/check_markdown_links.py`.

## Exit Criteria

- All users can browse every canonical IMDb row; per-user state is isolated, and admin-only actions are hidden and rejected for non-admin users.
- Filters/sorts match the specified defaults and API ordering, page counts follow the visibility toggles, and the compact award line remains readable at desktop/mobile widths.
- Favorite, ignore, rating, and details flows match the approved rules. Admin add and forced refresh UI remain deferred.
- Focused web tests, affected server integration/unit tests, server build, web lint/build, and Markdown link checks pass. No unrelated source behavior changes.