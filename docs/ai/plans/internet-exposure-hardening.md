# Internet Exposure Security Hardening

## Goal

Make MediaDock usable only by active, administrator-approved accounts before any internet exposure. Enforce that rule in the API, present a sign-in/account-status experience to everyone else, and keep the origin inaccessible except through explicitly approved network paths.

The security findings and current evidence are in the [reassessed internet exposure audit](../SECURITY_AUDIT_2026-10-26.md). This plan is a delivery sequence, not approval to change production.

## Planning Layout

| Phase | Scope | Detailed plan |
| --- | --- | --- |
| 1 | Application access and login-only UI | [Phase 1](internet-exposure-phase-1-access.md) |
| 2 | API error handling, CSRF, and abuse controls | [Phase 2](internet-exposure-phase-2-api-defenses.md) |
| 3 | Origin, browser, host, and development safeguards | [Phase 3](internet-exposure-phase-3-runtime-host.md) |
| 4 | Deployment and external launch gate | [Phase 4](internet-exposure-phase-4-launch-gate.md) |

Phase 1 defines the authorization and anonymous-route contract. Phases 2 and 3 can proceed independently once that contract is agreed. Phase 4 depends on all remediation phases and operator approval.

## Scope and Constraints

- Application access means a validated Google identity linked to an active, approved MediaDock account. Google authentication by itself, Cloudflare Access, an anti-forgery token, or a hidden React route does not authorize API use.
- Preserve administrator approval for new registrations. Pending, unknown, conflicted, and deactivated accounts must not access application data.
- Do not expand the origin bind or firewall allowlist as a workaround. Keep PostgreSQL unpublished; trust forwarded headers only from the dedicated proxy network.
- Do not expose the restored-data Vite/API development workflow. Mock authentication is development-only and must remain loopback-only.
- Keep production secrets, host addresses, and real identity values out of Git. Do not run production migrations, deployments, firewall changes, or credential rotation as part of implementation without separate operator approval.
- The active-account UI gate is currently an uncommitted worktree change. Inspect and preserve it; do not assume it is deployed. Browser-delivered assets need not be private unless hiding application code is an explicit requirement; API data and actions must be protected regardless.

## Dependencies and Decisions

- Before implementation, agree on the exact anonymous route allowlist and whether Cloudflare Access remains a separate required gate.
- Decide rate-limit values from expected legitimate traffic and the edge provider's capabilities; do not choose arbitrary production thresholds in code.
- Confirm whether public registration requests remain enabled. If retained, require Google authentication, admin approval, abuse limits, and a documented account recovery path.
- Confirm a protected host-only administrator recovery procedure before making enforced authorization mandatory.
- Verify current deployed SHA and host configuration from the host; repository state is not deployment evidence.

## Master Acceptance Criteria

- Anonymous clients can reach only the explicitly approved sign-in/session/account-request surface and internal health checks; version, catalog, personal data, configuration, operations, jobs, and deployment data are not anonymous.
- Unknown, pending, conflicted, and deactivated identities cannot use the app. Active users can use only user-level capabilities; active admins can use administrator capabilities. The server resolves identity, state, and role on each protected request.
- Browser navigation shows only the intended welcome/sign-in or account-status view until access is approved. Direct API calls receive the same enforcement as UI-driven requests.
- Every cookie-authenticated state change has the selected CSRF/origin defense; unexpected 500 responses disclose no exception detail; abuse controls and response/cache headers have tests.
- The Development mock workflow cannot be reached from an untrusted interface and is never enabled in Production.
- Cloudflare/TLS/origin/firewall checks pass from both an external network and an untrusted LAN source, and the deployed SHA is recorded.
- All applicable repository test suites and deployment gates pass. No production exposure change proceeds without backup, operator approval, and a verified recovery path.

## Current State

The 2026-10-30 audit found API enforcement still defaults off, the application UI gate present only in uncommitted changes, direct-origin exposure unverified, incomplete CSRF coverage, raw unhandled-exception details in 500 responses, no application rate limiting, and host/browser controls requiring verification. The local Development mock-auth/Vite proxy combination is a conditional risk when the dev server is exposed. Focused UI and authentication tests passed during that audit; production host state was not inspected.

Phase 1 repository implementation has started; no production operation has started under this plan. Do not create a progress tracker unless work spans sessions.