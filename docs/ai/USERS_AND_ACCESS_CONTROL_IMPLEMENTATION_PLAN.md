# Users and Access Control: Implementation Plan

**Status:** Planned; implementation not started  
**Source design:** [Users and access control plan](USERS_AND_ACCESS_CONTROL_PLAN.md)

**Prerequisite:** The [Google authentication and registration requests plan](GOOGLE_AUTHENTICATION_PLAN.md) is complete for source implementation and documentation. It adds optional OIDC sign-in and identity persistence without enforcing authorization or changing shared personal data. Reuse its schema, session, linking, registration-request, and bootstrap flows; do not recreate them here. Google Cloud client provisioning and the production LAN HTTPS callback remain operator setup gates, not blockers to starting this plan's code work.

This document turns the source design into bounded implementation sessions. Each session should end with its acceptance checks passing and a short handoff recording completed work, decisions, and the next session. Do not deploy or migrate production data as part of a coding session.

## Analysis and Safety Decisions

Before the Google authentication foundation, the implementation has one shared `favorite_movies` row per `title_id` and one shared `personal_ratings` row per IMDb ID. After that foundation, optional authentication identifies a caller, but the API still has no authorization enforcement and those records remain shared. Consequently, adding identity or ownership columns alone does not make personal data private, and a client-supplied user ID must never establish ownership.

The original source design placed user/role management before enforcement. That would be unsafe even after optional sign-in exists: without authorization, any client that reaches the API could create accounts, change roles, or deactivate an account. A persistent warning explains the risk but does not prevent it.

**Required safety adjustment:** while authorization is disabled, preserve the existing single-profile behavior under one bootstrap owner. Do not expose multi-user selection, user/role management, or APIs that accept an owner ID. The warning remains visible, and the existing loopback/LAN boundary remains mandatory. Provision and link the first administrator through an explicit operator-controlled bootstrap procedure; expose normal user administration only after authorization is enforced. Registration requests created by the prerequisite plan do not grant permissions and must not be approved through an unprotected application endpoint.

Treat these decisions as a gate, not implementation assumptions. Close them in Session 0:

- Verify the implemented Google OIDC provider and same-origin server-managed session against the prerequisite plan and source. Reuse its validated issuer/subject identity, verified-email policy, 12-hour non-sliding protected cookie, anti-forgery protection, account-link confirmation, and host-only bootstrap. Resolve implementation gaps before enforcement; do not duplicate the login flow.
- Accepted email policy. Email is a normalized, unique account attribute, not durable provider identity. Persist provider identity by validated `issuer` plus `subject`.
- Reuse the existing operator-supplied one-shot `bootstrap-admin` Compose tool, populated only from the validated session response; it does not verify Google claims itself. Reuse the explicit one-time server-derived account-link confirmation. Do not add public bootstrap, approval, or role-management endpoints. Registration-request review remains host/operator-controlled until protected administration exists.
- Deactivation and deletion policy. Recommended initial behavior: deactivate and retain personal rows; do not cascade-delete or reassign them implicitly.
- Initial roles/capabilities. The identity schema uses `admin` and `user`; administrators do not read or edit another user's personal rows by default. Authorization capabilities and enforcement remain to be decided here.
- Exact routes available anonymously, the explicit authorization-disabled configuration, and the conditions under which enforcement may be enabled.

Record the decisions in the source design or a short decision record before Session 1. If any decision changes the ownership or identity model, update this sequence before coding.

## Session Sequence

### Session 0: Close Decisions and Baseline

**Goal:** make the identity, bootstrap, retention, and rollout decisions above actionable.

**Work:** inspect the current route inventory in [API contracts](API_CONTRACTS.md); confirm the authorization matrix in the source design; record the remaining ownership, deactivation, and capability decisions. Verify the prerequisite OIDC/session, email policy, account-linking flow, bootstrap procedure, and registration-request boundary against source, and reuse them unless a concrete implementation gap is found. Confirm the project remains loopback-bound by default and LAN access remains limited to the configured interface/firewall allowlist.

**Exit checks:** no unresolved decision affects the user schema, ownership migration, or authentication flow. No code or production changes are required.

### Session 1: Identity Schema and Legacy Ownership Migration

**Goal:** add per-user ownership while preserving the current single-profile data exactly, reusing the identity schema from the prerequisite plan.

**Work:** reuse the existing `User` and external identity entities/configurations, normalized-email and `(issuer, subject)` uniqueness, `given_name` and `family_name`, status, role, registration-request records, and OIDC login/session. Change favorites to `(user_id, title_id)` and ratings to `(user_id, imdb_id)`, adding restrictive ownership foreign keys. Add an additive EF migration; leave baseline/old migrations unchanged. Backfill all existing favorites and ratings to exactly one explicitly provisioned bootstrap owner, preserving favorite markers, origin flags, timestamps, rating values, and rating timestamps. Fail safely if the bootstrap owner cannot be determined; never drop, duplicate, or distribute legacy rows. Keep API behavior single-profile during this intermediate session; do not add user administration or owner selection.

**Exit checks:** PostgreSQL integration tests apply migrations to a fresh database and upgrade a pre-change schema containing representative favorite/rating rows. Tests prove each old row is retained exactly once under the bootstrap owner, constraints reject duplicate ownership keys, and no unrelated catalog rows gain a user owner. Verify rollback/data-loss implications in a disposable database only.

### Session 2: Authentication and Controlled Bootstrap

**Goal:** verify and complete the trusted server-side user context from the prerequisite plan without yet turning on authorization enforcement.

**Work:** reuse and verify the existing OIDC/session implementation, validated issuer/audience/signature/nonce/state and verified claims, explicit bootstrap/account linking, sign-in/sign-out, and server-side mapping from provider identity to internal `users.id`. The bootstrap tool trusts host-supplied values, so obtain them from the authenticated session response; do not duplicate the Google login flow or expose bootstrap, approval, or role mutation publicly. Close only identified gaps. Resolve active state and role from current server-side data when authorization is enforced (or use an equivalently immediate revocation mechanism), rather than trusting stale client role state. When authorization is enabled, missing, unknown, or deactivated identity must fail closed; never fall back to the bootstrap owner. Keep the authorization-disabled mode explicitly configured and visibly unsafe.

**Exit checks:** automated tests cover valid/invalid provider identity, failed or repeated bootstrap/linking, unknown identity, deactivated account, logout, CSRF rejection where applicable, and the prohibition on client-selected user IDs. Use a test authentication handler or local test issuer; do not depend on live Google credentials.

### Session 3: Enforce the System Access Matrix

**Goal:** apply server-side policies consistently to every existing route before exposing multi-user administration.

**Work:** define named capabilities/policies and apply them to endpoint groups. Authenticated users may read approved shared catalogs and awards. Personal routes require an active authenticated user and derive ownership from `CurrentUser`. System writes and operations are admin-only, including sources/settings, imports/enrichment/ingestion producers, operational history where classified admin-only, and deployment control. Keep only the explicit Session 0 anonymous routes (for example, liveness/readiness) anonymous. UI visibility is not authorization. Ensure the disabled mode is explicit, retains the persistent warning, and is never mistaken for enforcement.

**Exit checks:** integration tests exercise unauthenticated, `user`, `admin`, unknown, and deactivated callers for every route group and representative write methods; verify correct 401/403 behavior and that deployment routes remain protected. Review the complete route inventory, including newly mapped endpoints, for an intentional policy or explicit anonymous designation.

### Session 4: Scope Favorites to the Authenticated Owner

**Goal:** make favorite operations truly per-user without changing shared title/catalog identity.

**Work:** update favorite queries, add/update/delete operations, and projections to include the server-derived owner key. Preserve source validation, independent markers, atomic/idempotent adds, pagination, and all existing semantics. Never accept an owner ID from query, route, or body. Keep `titles`, occurrences, and awards shared.

**Exit checks:** PostgreSQL API tests prove two users can independently favorite the same title and that one user cannot list, read, modify, or delete the other's favorite by changing a title ID or request payload. Existing favorite behavior tests continue to pass for one user.

### Session 5: Scope Personal Ratings and Import to the Owner

**Goal:** isolate ratings and preserve the current safe merge semantics per account.

**Work:** scope import reads/writes and result counts to the authenticated owner. Keep the bounded in-memory parser, whole-file validation before mutation, merge-only behavior, and row-level missing-rating reporting. Do not change shared OMDb title ratings or delete personal ratings absent from a subsequent export.

**Exit checks:** tests prove two users can store different ratings for the same IMDb ID; importing as one user cannot read, update, or affect the other user's data. Retain coverage for malformed files, size/row bounds, unchanged/updated/added counts, and no partial writes on invalid input.

### Session 6: User Administration API

**Goal:** implement account lifecycle operations only after Sessions 2 and 3 provide real identity and enforcement.

**Work:** add admin-only, paginated/searchable list, create/invite, role-change, and deactivate operations. Creating a user record must not itself create a trusted provider identity. Enforce normalized-email uniqueness, valid roles, active-state rules, and the last-active-admin invariant transactionally, including concurrent requests. Preserve personal rows on deactivation. Record security-relevant lifecycle changes without logging credentials or tokens.

**Exit checks:** API tests cover `user` denial, admin success, duplicate normalized email, invalid role, repeated deactivation, identity-link boundary, last-admin protection under concurrent changes, and retained personal rows. Confirm ordinary admins cannot inspect or edit another user's favorites/ratings unless an explicitly approved audited capability was added.

### Session 7: Web User Management and Open-Mode Warning

**Goal:** connect the existing optional sign-in UI to enforced authorization and add the protected user-administration workflow.

**Work:** reuse the sign-in/sign-out and authenticated-session loading from the prerequisite plan; handle 401/403 without stale privileged UI. Add the Users configuration view for authorized admins, including registration-request review, role/state, confirmation, and last-admin error handling. Show the persistent, non-dismissible access warning whenever authorization is disabled, with wording matching the actual loopback or LAN boundary. Do not show account/role controls as protected in disabled mode. Ensure existing favorites and ratings UI calls operate only on the current signed-in profile.

**Exit checks:** focused web tests cover signed-out, user, admin, deactivated/expired session, disabled-mode warning, user lifecycle actions, and API failures. Run web lint, tests, and build; verify no sensitive tokens or provider credentials are placed in browser storage or logs.

### Session 8: Security Review, Rollout, and Contract Updates

**Goal:** verify the complete cross-layer behavior and prepare a controlled rollout.

**Work:** update [API contracts](API_CONTRACTS.md), [data contracts](DATA_CONTRACTS.md), [architecture](ARCHITECTURE.md), [testing guide](TESTING.md), [implementation status](IMPLEMENTATION_STATUS.md), and the related [favorites](FAVORITE_MOVIES_PLAN.md) and [IMDb ratings](IMDB_PERSONAL_RATINGS_PLAN.md) plans. Update the local runbook/configuration examples with secret handling, OIDC callback setup, bootstrap procedure, migration ordering, auth-mode behavior, backup/restore, and recovery steps. Keep credentials out of the repository. Run a security review of all routes and identity-linking paths, then test migration and sign-in in an isolated/staging environment with a backup before any production rollout.

**Exit checks:** full server build and unit/integration suites, web lint/tests/build, migration tests from both empty and representative existing schemas, and the complete access matrix pass. Confirm at least one administrator can sign in before enabling enforcement; prove disabled mode cannot be mistaken for protection and the warning remains visible. Production migration/deployment requires the normal operator approval and deployment gate; it is not part of this plan's coding sessions.

## Cross-Session Acceptance Criteria

- Every personal-data query and mutation is scoped using a server-derived internal user ID. No client-controlled owner ID establishes access.
- Two accounts can maintain independent favorites and ratings for the same canonical title/IMDb ID; shared catalog data remains shared.
- Legacy personal rows are attributed once to the designated bootstrap owner and survive migration without losing marker, origin, rating, or timestamp values.
- A regular user cannot administer accounts or mutate system settings, sources, imports, jobs, history, or deployment actions. An administrator cannot access another user's personal data by default.
- Unknown and deactivated identities are denied; role changes/deactivation take effect immediately; last active administrator cannot be removed accidentally or concurrently.
- Authentication-disabled operation is clearly and persistently identified as unprotected and remains within the documented network boundary. It is not represented as multi-user security.
- Tests use disposable PostgreSQL and stubbed/local identity infrastructure. They do not use production data, live provider credentials, or live RSS/OMDb services.

## Session Handoff

At the end of each session, record the completed session number, changed files, test commands/results, decisions or blockers, and the next session's entry point. Do not begin a later session while the current session's exit checks are failing; update this plan if implementation evidence requires a design change.
