# Internet Exposure Phase 1: Access and UI

## Goal

Require an active linked MediaDock account in the API and make the browser show only sign-in or account-status content until the account is approved.

## Tasks

- Inspect the current uncommitted changes in `web/src/App.tsx`, its tests, and styles; preserve the work and close any remaining route, loading, sign-out, or account-switch gaps.
- Define the anonymous/authenticated route matrix. Permit only the minimum Google OIDC login/callback, session, anti-forgery, link/registration, and logout flow. Keep health checks internal and do not expose version or application data by default.
- Make production authorization fail closed: require Google sign-in configuration and authorization enforcement for internet-facing production, while retaining a deliberate local-development path. Do not make open mode the account-recovery mechanism.
- Require a validated identity linked to an active account for shared catalog/award reads and personal routes. Keep administrator routes restricted to an active admin. Verify that every mapped endpoint is covered by an explicit policy or the fallback policy, with narrow anonymous exceptions.
- Keep registration pending until an administrator approves it. Ensure user-facing status does not reveal other users or registration requests.
- Document safe first-admin bootstrap and host-only recovery; remove bootstrap values after provisioning.

## Verification

- `python -B scripts/run_tests.py web --filter App.test.tsx`
- `python -B scripts/run_tests.py server-integration --filter "Category=Api"`
- Test anonymous, authenticated-unlinked, pending, deactivated, active-user, and active-admin identities against route groups and direct API calls. Verify UI gating is not treated as the server security control.
- Build the production bundle and inspect the deployed login, callback, and sign-out flow over the exact HTTPS hostname.

## Exit Criteria

- Anonymous browser sessions show no application navigation or catalog content; unapproved identities see only account status and allowed account actions.
- Protected API reads and writes fail closed for anonymous/unlinked/pending/deactivated identities, regardless of UI route.
- Active users and admins receive only their respective capabilities, resolved from current database state.
- Production configuration cannot accidentally start with access enforcement disabled or Google authentication unavailable. Local-only development behavior remains explicit and bounded.
- Relevant web and API tests pass; no production change has been made without a separate approved deployment.