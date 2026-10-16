# User Settings and Personal Data Migration Plan

**Status:** Session 1 schema/backfill and Session 2 authorization gate complete; favorites/ratings remain unavailable until their owner-scoped API sessions.
**Updated:** 2026-10-16
**Prerequisite:** Reuse the identity and authorization work in the [users and access control implementation plan](USERS_AND_ACCESS_CONTROL_IMPLEMENTATION_PLAN.md). This plan does not recreate Google sign-in, user records, or account administration.

## Goal and Scope

Move genuinely personal data and preferences from the installation-wide profile to the signed-in user's profile, across PostgreSQL, API behavior, and navigation. Keep the shared title/award catalog and system operations shared. Do not expose multi-user data until the API derives an active owner from the validated server-side session and enforces authorization; hiding menu items is not security.

The migration branches directly on the number of rows in `users`. If there are no users, remove the ownerless legacy favorites and ratings. If there is one user, assign the rows to that user. If there are multiple users, copy the old shared profile to each existing user so everyone starts with the data they could already see.

## Current Inventory and Target Ownership

| Data or setting | Current implementation | Target ownership and action |
| --- | --- | --- |
| Favorites | `favorite_movies` has one row per `title_id`; includes `ToWatch`, `ToDownload`, source-origin flags, and timestamps. | Per user. Key by `(user_id, title_id)`. Keep both independent markers and origin/timestamp fields on that user's row. |
| Personal IMDb ratings | `personal_ratings` has one row per `imdb_id`; distinct from the shared OMDb `Title.ImdbRating`. Import is a merge-only upload. | Per user. Key by `(user_id, imdb_id)`. Keep the existing validation and add/update/unchanged merge behavior within the current user's rows. |
| Personal rating filters | A personal-rating import exists. `excludeRated` and saved per-user catalog-filter defaults are not implemented; catalog filters remain transient. | No saved filters in this migration. If `excludeRated` or saved defaults are added later, they must use only the current user's ratings and be separately approved. |
| Catalog filter values | Catalog views hold filter inputs in view state; no saved per-user catalog-filter preference was found. | Do not migrate transient filters. Add per-user saved defaults only after their scope and reset behavior are specified. |
| `MinMovieRating`, `MinSeriesRating`, `MinImdbVotes` | Stored in the singleton `settings` row and editable in Configuration > Torrent settings. | Retain as legacy singleton settings for compatibility only; see the Session 0 decision below. Do not copy them to user preferences. |
| `ExcludedGenres`, `ExcludedCountries` | Stored in singleton `settings`; the RSS repository loads these into `IngestionMatchSettings`, and ingestion applies them through `MatchPolicy`. | Global admin settings. They affect the shared ingestion/catalog result, not an individual user's view. A future personal catalog filter must use separate fields and logic. |
| RSS profile URLs and feed types | Shared `sources` rows select system parsing profiles and feeds. | Global admin configuration. Do not give each user independent ingestion that mutates the shared catalog. |
| OMDb key, daily request limit, and usage | Shared provider configuration and UTC usage budget. | Global admin configuration. A user-specific value would conflict with the shared provider budget and job dispatcher. |
| Dataset imports, enrichment, scans, history, deployment | Operate on installation-wide awards/catalog data and services. | Global/admin operations. Keep behind server-side admin authorization. |
| Titles, occurrences, awards, metadata cache | Shared canonical and operational data. `Title.ImdbRating` is provider metadata, not a personal score. | Remain shared; do not add `user_id` to these records. |

### Minimum-Rating Settings: Verified Behavior and Session 0 Decision

The current `MinMovieRating`, `MinSeriesRating`, and `MinImdbVotes` fields are stored and returned by `/api/settings`, and the form writes them back. The RSS matching settings passed into ingestion contain only excluded countries and genres; the three minimum fields have no consumer in the RSS pipeline. The catalog views do not load `/api/settings` to initialize their rating filters; filter inputs are transient and use catalog rating bounds. Therefore these fields currently have storage and settings-UI/API round-trip behavior only: they are neither RSS rules nor catalog defaults.

**Session 0 decision:** Keep the three values as legacy singleton settings for compatibility through the ownership migration. Do not copy or convert them into user preferences, make them RSS rules, or remove/change their current settings API in this migration. If personal catalog defaults are wanted later, add separately named per-user preferences and use them only to prefill that user's filters. Any global RSS-threshold behavior or deprecation/removal requires a separate product decision and implementation.

## Ownership and Migration Rules

1. Add an additive EF Core migration. Do not edit the baseline or previously applied migrations and do not reset the database.
2. Branch on the number of rows in `users`, regardless of account status. With zero users, delete any legacy rows from `favorite_movies` and `personal_ratings`, then apply the ownership constraints.
3. With exactly one user, backfill every legacy favorite and rating to that user.
4. With more than one user, copy every legacy favorite and rating to every existing user. This preserves the old shared-profile view at the point users become isolated; the composite keys make each copy independent after migration.
5. Preserve favorite markers, origin flags, timestamps, rating values, and rating timestamps in each branch. Deactivation retains personal rows; user deletion/cascading deletion remains disallowed until a retention policy is approved.
6. Do not choose one arbitrary user or require a separate operator-selected owner path. Treat rollback after multi-user writes as unsupported for automatic merging because users may have changed their copies independently.

### Target Schema

- `favorite_movies`: add required `user_id -> users.id` with restrictive deletion; primary key becomes `(user_id, title_id)`. The same title can be favorited independently by multiple users.
- `personal_ratings`: add required `user_id -> users.id` with restrictive deletion; primary key becomes `(user_id, imdb_id)`. Keep normalized IMDb IDs and the existing 1..10 constraint.
- Keep `titles`, occurrences, awards, sources, and system settings shared. Do not add ownership to `Title` or use a client-supplied `userId` as an ownership claim.
- Add a separate user-settings/preferences table only for fields approved as user preferences. Prefer typed columns and explicit names over reusing global setting names with changed semantics. Avoid creating an empty generic settings framework before the preference list is decided.

## API and Logic Changes

- Resolve the current internal `users.id` from the validated identity on the server for every personal request. Require an active account; unknown, anonymous, or deactivated callers receive the appropriate authorization failure. Never fall back to a default/legacy owner.
- Do not accept an owner ID in a route, query, or request body. Admin status does not grant access to another user's favorites or ratings by default.
- Scope favorite list/count/page/add/update/delete queries by the resolved owner, preserving source checks, idempotent additions, marker independence, and existing pagination semantics.
- Scope personal-rating import, reads, and any `excludeRated` filter by owner. Preserve whole-file validation before writes, bounded in-memory upload, merge-only semantics, and reporting of skipped missing-rating rows. Do not change shared OMDb title ratings.
- Split global settings contracts from personal preferences. Keep RSS exclusions and provider settings in admin-owned APIs. Put only approved personal preferences in a current-user API (for example, `/api/me/preferences`); never expose a generic settings write that can cross ownership domains.
- On account changes or sign-out, refresh or clear client-side favorites/ratings state so data from a previous session cannot remain visible.
- Reuse the authorization matrix in the access-control implementation plan. Finish server enforcement before enabling multi-user data access; client navigation is only a convenience.

## UI and Navigation Split

Today, Configuration contains **System**, **Torrent settings**, **Ingestion**, **Personal IMDb ratings**, **OMDb provider**, and **Users**. Move the personal ratings workflow out of Configuration, and keep global controls together under a clearly admin-only **Administration** or **Configuration** section.

- **User/account menu:** personal account/profile actions, personal IMDb ratings import/management, and approved personal preferences. Favorites remain easy to reach in the library navigation, but are fetched and mutated for the signed-in user only.
- **Admin menu:** RSS source URLs and global matching rules; award imports/enrichment and shared scan controls; OMDb key, quota, and usage; user administration; system version/deployment; and global operational history.
- **Shared library navigation:** Catalog, Oscar catalog, and Golden Globes catalog remain shared read views. Personal controls shown inside them, such as favorite actions or a personal-rating filter, use the current user's data.
- Use the current user's server-projected role/capabilities to decide whether to render the admin menu, refreshing that state after account changes. Enforce every action on the API regardless of menu visibility. Keep an explicit access warning while authorization is disabled.
- Preserve a redirect from the existing personal-ratings configuration route if route compatibility is useful; do not preserve an anonymous write path for personal data.

## Execution Sessions

Each session ends with a short handoff: completed work, files changed, test commands/results, decisions/blockers, and the next entry point. Do not perform a production database migration or deploy as part of a coding session.

### Session 0: Close Semantics and Scope

**Decision record (2026-10-16):**

- `MinMovieRating`, `MinSeriesRating`, and `MinImdbVotes` remain legacy singleton settings for compatibility only. They are not personal catalog defaults or active RSS rules, and the ownership migration will not copy, reinterpret, or remove them.
- No saved per-user catalog filters are in scope. `excludeRated` is not implemented and remains a separate future feature; if added, it must use only the current user's ratings.
- Deactivation retains personal rows. Account deletion, cascading deletion, and implicit reassignment remain unsupported until a separate retention policy is approved.
- Count all rows in `users`, regardless of status: zero users deletes legacy favorites/ratings, one user receives them, and multiple users each receive a copy.

**Work:** verified the settings, RSS matching, catalog, and account-status code paths and recorded the scope decisions above. No schema, API, code, or production changes are made in this planning session.

**Exit checks:** the threshold fields have an explicit non-personal, non-RSS meaning for this migration; saved-filter scope and deactivation retention are recorded; all user-count branches have defined outcomes; no production write is performed.

**Handoff:** Changed `docs/ai/USER_SETTINGS_MIGRATION_PLAN.md`; no tests were run because this was documentation-only. Verified the settings round-trip, RSS matching input, lack of saved-filter/`excludeRated` code, and status-only deactivation path. No production data was touched. Next entry point: Session 1 schema migration and PostgreSQL migration tests.

### Session 1: Schema and Legacy Backfill

Add the compound ownership keys and restrictive user foreign keys for favorites and personal ratings. Implement the user-count branches: delete legacy rows when there are no users, backfill to the sole user when there is one, and copy rows to every user when there are multiple. Do not copy the old singleton `settings` row or create personal catalog preferences; keep the existing settings fields unchanged.

**Exit checks:** PostgreSQL integration coverage for a fresh schema, legacy rows with zero users (deleted), one user (backfilled), and multiple users (copied to each), preserving all row values in the applicable branch. Run the narrow suite with `python -B scripts/run_tests.py server-integration --filter "Category=Persistence"`; Docker is required.

**Handoff:** Added required `user_id` ownership, compound keys, and restrictive user foreign keys for favorites and personal ratings. The additive migration deletes ownerless rows when there are no users, assigns legacy rows to the sole user regardless of status, and copies every legacy row to all users when there are multiple. PostgreSQL tests cover fresh schema, all three branches, preservation of favorite markers/origins/timestamps and rating values/timestamps, per-user key uniqueness, and restrictive user deletion. `python -B scripts/run_tests.py server-integration --filter "Category=Persistence"` passed (23/23). The singleton settings row and its fields remain unchanged; no personal preferences were created. No production migration or deployment was performed; no blockers. Next entry point: Session 2 current-user context and authorization gate.

### Session 2: Current-User Context and Authorization Gate

Complete/reuse the server-derived active-user context and authorization policies from the access-control plan before exposing per-user endpoints. Protect all existing admin/global mutation and operation routes. Ensure session/current-user role or capabilities are resolved from current server state, not trusted from browser input or stale client role claims.

**Exit checks:** anonymous, unknown, and deactivated callers fail closed when enforcement is enabled; ordinary users cannot call admin/global writes; personal endpoints fail closed in every mode until owner scoping exists; no endpoint accepts a client-selected owner ID. The explicit disabled mode remains open for other routes and visibly warned.

**Handoff (2026-10-16):** Added an explicit `ACCESS_CONTROL_ENFORCED` switch, a server-derived active-user context keyed by validated `(issuer, subject)`, and current-database role checks on each request. Enforced mode requires active users for shared catalog/award reads and admins for global configuration, user administration, operations, jobs, deployment, and manual Golden Globes links. Unknown/deactivated identities fail closed; admin mutation services also reject missing actors transactionally rather than falling back to open mode. Favorites and ratings stay denied in all modes until owner-scoped implementation, and no client owner ID is used. `/api/auth/session` projects current role and enforcement state; the persistent warning follows the server flag. Changed API auth/policy code, access integration tests, web session types/banner/tests, Compose and `.env.example`, API/architecture/runbook documentation, and the access-control plans. Verification: focused admin-policy and missing-actor tests passed 1/1 each; `python -B scripts/run_tests.py server-integration --filter "Category=Api"` passed 37/37; `python -B scripts/run_tests.py web` passed 101/101; `npm --prefix web run lint` succeeded with five warnings outside the changed files; `npm --prefix web run build` succeeded; `git diff --check` was clean. No production migration, configuration change, or deployment was performed. No blockers. Next entry point: Session 3 per-user favorites.

### Session 3: Per-User Favorites

Scope favorite reads and mutations, API projections, and any favorite-backed catalog controls by the authenticated owner. Keep canonical titles and torrent availability shared. Refresh client state when the signed-in account changes.

**Exit checks:** two users can independently add the same title, choose different `ToWatch`/`ToDownload` values, and remove/update only their own rows. API tests cover list/count/page isolation and attempts to access another user's row by ID.

### Session 4: Per-User Ratings and Approved Preferences

Scope personal rating import and all rating lookups by user. Add only separately approved user preferences; this migration does not create catalog preferences or move minimum-threshold fields. Any later personal catalog defaults must use explicitly named fields and apply only to UI filter prefills. Keep global exclusions/provider settings in the admin settings contract. Add `excludeRated` only as a user-scoped filter, if approved.

**Exit checks:** two users can rate the same IMDb ID differently; importing for one does not affect or reveal another user's rows; existing import validation/merge guarantees remain. Any prefilled filter is proven to load the current user's value and not alter RSS ingestion.

### Session 5: User and Admin Menus

Move Personal IMDb ratings and approved personal preferences into the user/account area. Keep Users, sources, ingestion, OMDb, deployment, and operational history in an admin area. Keep shared catalogs in shared navigation. Update the session projection for navigation if needed, while retaining server-side authorization on every route.

**Exit checks:** focused web tests cover anonymous/signed-in/user/admin menus, switching accounts, personal ratings access, and API 401/403 handling. No user-only control remains under the global admin settings menu, and no admin action is accessible merely by direct URL.

### Session 6: Contract, Security, and Rollout Handoff

Update the data/API contracts, architecture, implementation status, testing guide, personal-ratings plan, and operator migration notes. Validate the complete vertical slice with migration integration tests; only then prepare a separately approved deployment.

**Exit checks:** all migration, isolation, authorization, UI, and import criteria pass; production rollout remains behind the normal operator approval and deployment gate.

## Verification Matrix

- Database: apply migrations from empty and representative existing schemas; verify legacy rows are deleted with zero users, backfilled with one user, and copied to every user when multiple users exist; verify composite uniqueness and restrictive user foreign keys.
- API: two-user favorites and ratings isolation; forged owner IDs ignored/rejected; anonymous/deactivated access denied; admin does not read another user's private data by default; import validation/merge behavior retained.
- UI: personal and admin menu separation; personal views reload on identity change; shared catalog stays available according to the access matrix; controls handle expired sessions and 401/403 responses.
- Use `python -B scripts/run_tests.py <suite> --filter "<focused filter>"` for repository tests. Use `server-integration` for PostgreSQL migration/API checks and `web` for React tests; the server integration suite requires Docker. Run the full `all` suite at the final implementation gate, not for this documentation-only planning change.
- Do not test by modifying production data, calling live RSS/OMDb, or using live Google credentials.

## Cross-Session Acceptance Criteria

- Favorites and personal ratings are isolated by server-derived user ownership; legacy rows follow the documented user-count rule: deleted for zero users, assigned to the sole user, or copied to all users when multiple users exist.
- Personal IMDb scores remain separate from shared OMDb ratings; shared titles and torrent/award data remain installation-wide.
- No API uses a client-provided owner ID, and no anonymous/default-owner fallback can expose personal rows.
- Global sources, ingestion behavior, provider credentials/budget, and operations remain admin-controlled and are not copied into every user's settings.
- `MinMovieRating` and related thresholds remain legacy singleton settings for compatibility through the ownership migration; they are neither personal UI defaults nor RSS rules. Any later use or deprecation is a separate decision.
- Personal account actions and admin/global controls are visibly separate in the UI, with server authorization as the enforcement boundary.

## Handoff Notes

At the end of each session, record the session number, changed files, test command/results, migration branch tested, settings-semantic decisions, blockers, and the next session's entry point. Update this plan when implementation evidence changes the inventory or migration behavior.