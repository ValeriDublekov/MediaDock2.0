# All IMDb Titles: Progress

Updated: 2026-11-05

## Current Slice

Implemented the canonical paged IMDb list API, server-side filters/sorts/totals, active-user-only personal-state projection, type-agnostic favorite/ignore/rating writes, and the web catalog with poster/table views, ratings, linked awards, and an IMDb-identity details dialog.

## Deferred

- Administrator candidate search/lookup/add for shared canonical metadata.
- Forced OMDb refresh, exact-ID cache bypass, targeted background job, deduplication index, and its migration.
- Admin candidate/refresh UI, polling, and outcome feedback.

Resume from Phase 1 tasks 7-8 and Phase 2 admin add/refresh items. Preserve owner-scoped `/manual-*` routes and existing movie/series-only recommendation behavior.

## Verification

- Before the scope reduction: `server-integration --filter "Category=Api"` passed 83/83. Re-run it against the reduced tree.
- Focused web catalog, route, API-client, favorite, ignore, and personal candidate-picker tests passed before removing deferred admin UI. Re-run focused tests and the full web suite.
- Still required for the active slice: server solution build, web lint/build, Markdown link check, final Git status/diff review.
- Integrated browser inspection did not mount the page in the available browser session; retry with a working local browser/API mock before claiming visual verification.
