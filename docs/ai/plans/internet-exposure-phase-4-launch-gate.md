# Internet Exposure Phase 4: Launch Gate

## Goal

Prove the approved security design on the exact release and host before broadening internet exposure.

Keep the evidence tracks separate: the [code review](../SECURITY_AUDIT_2026-10-26.md) defines application changes/tests; the [deployment review](../SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md) defines infrastructure and operational requirements. This launch gate verifies their combined result on the installed release.

## Preconditions

- Phases 1-3 meet their exit criteria.
- Operator confirms the target hostname, Cloudflare Access policy, Google OAuth origin/callback, registration policy, origin bind, and intended user/admin roles.
- Current deployed SHA, migration state, validated backup, recovery steps, and rollback owner are recorded. Obtain explicit approval for production deployment and any migration/network/secret operation.
- The release is from a clean, trusted checkout and passes the repository's deployment test gate.
- The audit's Production login-delivery defect is fixed: anonymous HTML/assets load, only sign-in/account status is rendered, and the same host independently denies protected API requests.

## Preflight Status (2026-10-30)

**NO-GO.** Phase 3 host checks are incomplete, and the last recorded deployed API allowed anonymous access to protected routes. Current source also has the Production login-delivery blocker described in the [audit](../SECURITY_AUDIT_2026-10-26.md).

Previously recorded read-only host checks found API image `33327386c4351a95f4b7d4f495ffbebd7636be91` running in Production with Google sign-in enabled but `AccessControl__Enforced=false`. Unauthenticated direct GETs to `/api/catalog`, `/api/admin/users`, `/api/deployment`, and `/api/version` returned HTTP 200; `/health/ready` also returned HTTP 200. PostgreSQL was loopback-bound; the API was bound to the LAN interface. These observations were not repeated during the latest source audit and must not be presented as a fresh host measurement. No production settings or services were changed.

Do not widen exposure. The deployed release fails the anonymous validation matrix. External and untrusted-LAN checks, the browser/API identity matrix, Cloudflare/TLS/OIDC verification, backup restore, exact-release deployment gate, image scanning, and operator sign-off remain outstanding. No production settings or services were changed.

## Validation Matrix

- Anonymous: only approved login/session/anti-forgery/account-request protocol paths work; catalog, awards, personal data, settings, administration, jobs, deployment, and version are unavailable.
- Authenticated but unlinked, pending, conflicted, or deactivated: no application data or operations; only appropriate account status, sign-out, and approved registration/link flow.
- Active user: shared read access and own personal data only; all administrator endpoints denied.
- Active admin: approved shared administration and operations work; personal data remains owner-scoped.
- Browser and API: real Production HTML/assets return 200 and the UI gate works on direct deep links without pre-approval business requests; API independently enforces identity. State changes reject cookie CSRF, uploads and queues enforce bounds, rate limits return expected errors, and private responses are not cached. When testing API authorization, supply valid CSRF tokens where needed so an earlier 400 does not mask a missing 401/403.
- Privileged boundaries: unrelated host identities cannot use the deployment socket; the runtime database role lacks administrative/DDL privileges; shared-catalog edits follow the explicitly approved user/admin policy.
- Network: public hostname passes only through approved HTTPS/Access controls; direct origin, alternate hosts, untrusted LAN, and PostgreSQL are denied. Recheck after container recreation/redeployment.

## Verification

- Run the repository deployment gate on the clean trusted release. Run current npm and NuGet vulnerability checks and record their sources/date; include container image scanning.
- Perform browser checks and direct HTTP checks from both trusted and untrusted network locations. Keep dated evidence tied to the deployed SHA without storing secrets or sensitive response bodies.
- Verify `/health/ready`, sign-in/OIDC, user approval, role denial, logout, and recovery using the deployed configuration. Do not call readiness alone proof of authorization or correct migrations.

## Exit Criteria

- Every blocker and high-priority finding in both the code and deployment reviews is resolved or has a documented, explicitly accepted mitigation before internet exposure.
- All validation matrix cases pass on the deployed SHA; direct-origin and router bypass checks are demonstrated from outside the trusted network.
- Backup restore and recovery procedures are verified, and the operator records approval and evidence.
- Cloudflare Access is not removed and origin exposure is not widened until these criteria pass.