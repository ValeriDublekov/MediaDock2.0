# Internet Exposure Security Hardening

## Goal

Make MediaDock usable only by active, administrator-approved accounts before any internet exposure. Enforce that rule in the API, present a sign-in/account-status experience to everyone else, and keep the origin inaccessible except through explicitly approved network paths.

The security findings and current evidence are in the [reassessed internet exposure audit](../SECURITY_AUDIT_2026-10-26.md). This plan is a delivery sequence, not approval to change production.

## Planning Layout

| Remaining scope | Detailed checklist |
| --- | --- |
| Runtime and host verification | [Phase 3](internet-exposure-phase-3-runtime-host.md) |
| Deployment and external launch gate | [Phase 4](internet-exposure-phase-4-launch-gate.md) |

Core application-access and API-defense controls are present in the candidate. Their remaining route, identity, CSRF, and abuse-control checks are listed below; this does not mean Phases 1 or 2 are accepted. Phases 3 and 4 remain open operational gates.

## Scope and Constraints

- Application access means a validated Google identity linked to an active, approved MediaDock account. Google authentication by itself, Cloudflare Access, an anti-forgery token, or a hidden React route does not authorize API use.
- Preserve administrator approval for new registrations. Pending, unknown, conflicted, and deactivated accounts must not access application data.
- Do not expand the origin bind or firewall allowlist as a workaround. Keep PostgreSQL unpublished; trust forwarded headers only from the dedicated proxy network.
- Do not expose the restored-data Vite/API development workflow. Mock authentication is development-only and must remain loopback-only.
- Keep production secrets, host addresses, and real identity values out of Git. Do not run production migrations, deployments, firewall changes, or credential rotation as part of implementation without separate operator approval.
- The active-account UI gate is present in the local candidate but is not deployed. Do not treat source code or passing tests as deployment evidence. Browser-delivered assets need not be private unless hiding application code is an explicit requirement; API data and actions must be protected regardless.

## Master Acceptance Criteria

- Anonymous clients can reach only the explicitly approved sign-in/session/account-request surface and internal health checks; version, catalog, personal data, configuration, operations, jobs, and deployment data are not anonymous.
- Unknown, pending, conflicted, and deactivated identities cannot use the app. Active users can use only user-level capabilities; active admins can use administrator capabilities. The server resolves identity, state, and role on each protected request.
- Browser navigation shows only the intended welcome/sign-in or account-status view until access is approved. Direct API calls receive the same enforcement as UI-driven requests.
- Every cookie-authenticated state change has the selected CSRF/origin defense; unexpected 500 responses disclose no exception detail; abuse controls and response/cache headers have tests.
- The Development mock workflow cannot be reached from an untrusted interface and is never enabled in Production.
- Cloudflare/TLS/origin/firewall checks pass from both an external network and an untrusted LAN source, and the deployed SHA is recorded.
- All applicable repository test suites and deployment gates pass. No production exposure change proceeds without backup, operator approval, and a verified recovery path.

## Current Blocker (2026-10-30)

Production runs image `33327386c4351a95f4b7d4f495ffbebd7636be91`; the local candidate `e6b1a415a49dbd3fbc2715a54ce3db5852d1f18b` is not deployed. The production API has Google sign-in enabled but `AccessControl:Enforced=false`. Unauthenticated direct GET requests to catalog, admin users, deployment, and version returned HTTP 200 from the host. No production configuration or service was changed. The host checks and remaining launch gates are recorded in [Phase 4](internet-exposure-phase-4-launch-gate.md#preflight-status-2026-10-30).

## Next Steps

1. Confirm the anonymous-route allowlist, registration policy, Cloudflare Access policy, rate-limit values, exact Google callback, and host-only administrator recovery/bootstrap procedure.
2. Verify the candidate's access behavior with anonymous, unlinked, pending, conflicted, deactivated, active-user, and active-admin identities. Check UI deep links, account status, sign-out, and account switching; API authorization must independently enforce the same roles.
3. Inventory every cookie-authenticated mutation, including multipart uploads, settings, admin, jobs, and deployment. Verify valid and invalid CSRF/origin cases; generic 500 responses with trace IDs; rate/concurrency limits and recovery; 413/429 contracts; request bounds; and resistance to spoofed forwarded client IPs. Fix only gaps found and rerun affected suites.
4. Complete the open Phase 3 host checks in [Phase 3](internet-exposure-phase-3-runtime-host.md), including direct-origin/network boundaries, HTTPS/OIDC and response behavior, Data Protection keys and restore, secrets/backups, image provenance, and loopback-only development access. Obtain separate approval before production network, firewall, secret, or service changes.
5. Run the repository deployment gate and current npm, NuGet, and container-image scans against the exact clean candidate SHA. Confirm migration state and a validated backup/restore and rollback path. With explicit operator approval, configure Google sign-in and `AccessControl:Enforced=true`, provision the first administrator, and deploy while retaining Cloudflare Access.
6. On the deployed SHA, run the remaining Phase 4 browser/API and external/untrusted-network checks and record dated evidence. Do not widen origin access or remove Cloudflare Access until every master acceptance criterion passes and the operator explicitly approves the change.