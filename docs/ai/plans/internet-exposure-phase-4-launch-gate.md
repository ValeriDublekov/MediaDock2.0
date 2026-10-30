# Internet Exposure Phase 4: Launch Gate

## Goal

Prove the approved security design on the exact release and host before broadening internet exposure.

## Preconditions

- Phases 1-3 meet their exit criteria.
- Operator confirms the target hostname, Cloudflare Access policy, Google OAuth origin/callback, registration policy, origin bind, and intended user/admin roles.
- Current deployed SHA, migration state, validated backup, recovery steps, and rollback owner are recorded. Obtain explicit approval for production deployment and any migration/network/secret operation.
- The release is from a clean, trusted checkout and passes the repository's deployment test gate.

## Validation Matrix

- Anonymous: only approved login/session/anti-forgery/account-request protocol paths work; catalog, awards, personal data, settings, administration, jobs, deployment, and version are unavailable.
- Authenticated but unlinked, pending, conflicted, or deactivated: no application data or operations; only appropriate account status, sign-out, and approved registration/link flow.
- Active user: shared read access and own personal data only; all administrator endpoints denied.
- Active admin: approved shared administration and operations work; personal data remains owner-scoped.
- Browser and API: UI gate works on direct deep links, API independently enforces identity, state changes reject CSRF, uploads and queues enforce bounds, rate limits return expected errors, and private responses are not cached.
- Network: public hostname passes only through approved HTTPS/Access controls; direct origin, alternate hosts, untrusted LAN, and PostgreSQL are denied. Recheck after container recreation/redeployment.

## Verification

- Run affected suites with `python -B scripts/run_tests.py`; include the complete affected API integration suite and web suite. Docker is required for server integration tests; do not skip them if relevant.
- Run the repository deployment gate on the clean trusted release. Run current npm and NuGet vulnerability checks and record their sources/date; include container image scanning.
- Perform browser checks and direct HTTP checks from both trusted and untrusted network locations. Keep dated evidence tied to the deployed SHA without storing secrets or sensitive response bodies.
- Verify `/health/ready`, sign-in/OIDC, user approval, role denial, logout, and recovery using the deployed configuration. Do not call readiness alone proof of authorization or correct migrations.

## Exit Criteria

- Every blocker and high-priority audit finding is resolved or has a documented, explicitly accepted mitigation before internet exposure.
- All validation matrix cases pass on the deployed SHA; direct-origin and router bypass checks are demonstrated from outside the trusted network.
- Backup restore and recovery procedures are verified, and the operator records approval and evidence.
- Cloudflare Access is not removed and origin exposure is not widened until these criteria pass.