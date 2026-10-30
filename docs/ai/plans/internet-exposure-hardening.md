# Internet Exposure Security Hardening

## Goal

Make MediaDock usable only by active, administrator-approved accounts before any internet exposure. Enforce that rule in the API, present a sign-in/account-status experience to everyone else, and keep the origin inaccessible except through explicitly approved network paths.

Application/dependency changes and their regression criteria are in the [code security review](../SECURITY_AUDIT_2026-10-26.md). Deployed-state, infrastructure, host-tooling and operational findings are in the separate [deployment review](../SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md). This master plan coordinates both tracks; it is a delivery sequence, not approval to change production.

## Planning Layout

| Remaining scope | Detailed checklist |
| --- | --- |
| Application code and dependency changes | [Code review](../SECURITY_AUDIT_2026-10-26.md) |
| Infrastructure and operational findings | [Deployment review](../SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md) |
| Runtime and host verification | [Phase 3](internet-exposure-phase-3-runtime-host.md) |
| Deployment and external launch gate | [Phase 4](internet-exposure-phase-4-launch-gate.md) |

Core API-access and request-defense controls are present in current source, but the Production static-host composition blocks anonymous delivery of the login page itself (audit A10). Their remaining route, identity, CSRF, and abuse-control checks are listed below; this does not mean Phases 1 or 2 are accepted. Phases 3 and 4 remain open operational gates.

## Scope and Constraints

- Application access means a validated Google identity linked to an active, approved MediaDock account. Google authentication by itself, Cloudflare Access, an anti-forgery token, or a hidden React route does not authorize API use.
- Preserve administrator approval for new registrations. Pending, unknown, conflicted, and deactivated accounts must not access application data.
- Do not expand the origin bind or firewall allowlist as a workaround. Keep PostgreSQL unpublished; trust forwarded headers only from the dedicated proxy network.
- Do not expose the restored-data Vite/API development workflow. Mock authentication is development-only and must remain loopback-only.
- Keep production secrets, host addresses, and real identity values out of Git. Do not run production migrations, deployments, firewall changes, or credential rotation as part of implementation without separate operator approval.
- The active-account UI gate is present in source, but Production currently returns 401 for anonymous HTML/assets. Provide an explicitly public login shell without weakening API enforcement. Do not treat source code or passing tests as deployment evidence. Browser-delivered assets need not be private unless hiding application code is an explicit requirement; API data and actions must be protected regardless.

## Master Acceptance Criteria

- Anonymous clients can reach only the explicitly approved sign-in/session/account-request surface and internal health checks; version, catalog, personal data, configuration, operations, jobs, and deployment data are not anonymous.
- Unknown, pending, conflicted, and deactivated identities cannot use the app. Active users can use only user-level capabilities; active admins can use administrator capabilities. The server resolves identity, state, and role on each protected request.
- Production browser navigation can load the login shell/assets with HTTP 200 and shows only minimal sign-in or account status until access is approved. No application navigation, data, or business requests appear before approval. Direct API calls remain independently protected.
- Every cookie-authenticated state change has the selected CSRF/origin defense; unexpected 500 responses disclose no exception detail; abuse controls and response/cache headers have tests.
- The Development mock workflow cannot be reached from an untrusted interface and is never enabled in Production.
- Cloudflare/TLS/origin/firewall checks pass from both an external network and an untrusted LAN source, and the deployed SHA is recorded.
- All applicable repository test suites and deployment gates pass. No production exposure change proceeds without backup, operator approval, and a verified recovery path.

## Current Blocker (2026-10-30)

The last recorded host preflight found image `33327386c4351a95f4b7d4f495ffbebd7636be91` with Google sign-in enabled but `AccessControl:Enforced=false`; anonymous catalog, admin-user, deployment, and version GETs returned HTTP 200. This is historical evidence, not a new host inspection. Current deployed state must be reconfirmed. See [Phase 4](internet-exposure-phase-4-launch-gate.md#preflight-status-2026-10-30).

The [code review](../SECURITY_AUDIT_2026-10-26.md) uses baseline source SHA `9f6ddee8055e0e311eb9fdcd399c01ca29f5d0ab`: normal Production startup fails closed without enforcement, but an isolated probe reproduced 401 for login HTML/JavaScript. Code work includes login delivery, development dependencies, shared-edit permissions, rate-limit coverage, CSP/cache policy and session/audit hardening. The [deployment review](../SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md) separately covers the socket, DB roles, origin controls, keys/backups and release evidence. Prior results were 183 application tests plus 3 deployment-tool tests; neither those nor the older validation-image scan are deployed-release acceptance. No production configuration or service was changed during the audit.

## Next Steps

1. Confirm the anonymous-route allowlist, registration policy, Cloudflare Access policy, rate-limit values, exact Google callback, and host-only administrator recovery/bootstrap procedure.
2. Fix Production login-shell/asset delivery without opening business APIs; simplify the anonymous screen to sign-in only and add a real Production static-host regression test. Verify anonymous, unlinked, pending, conflicted, deactivated, active-user, and active-admin identities, including deep links, account status, sign-out, and account switching.
3. Inventory every cookie-authenticated mutation, including multipart uploads, settings, admin, jobs, and deployment. Verify valid and invalid CSRF/origin cases; generic 500 responses with trace IDs; rate/concurrency limits and recovery; 413/429 contracts; request bounds; and resistance to spoofed forwarded client IPs. Fix only gaps found and rerun affected suites.
4. Complete the selected code changes and regression criteria from the code review, including dependency patches and the shared-edit permission decision. Separately address deployment findings in [Phase 3](internet-exposure-phase-3-runtime-host.md): socket callers, database privileges, network denial, HTTPS/OIDC, keys/secrets/backups and restore, image provenance, and private development access. Obtain separate approval before production database-role, network, firewall, secret, or service changes.
5. Run the repository deployment gate and current npm, NuGet, and container-image scans against the exact clean candidate SHA. Confirm migration state and a validated backup/restore and rollback path. With explicit operator approval, configure Google sign-in and `AccessControl:Enforced=true`, provision the first administrator, and deploy while retaining Cloudflare Access.
6. On the deployed SHA, run the remaining Phase 4 browser/API and external/untrusted-network checks and record dated evidence. Do not widen origin access or remove Cloudflare Access until every master acceptance criterion passes and the operator explicitly approves the change.