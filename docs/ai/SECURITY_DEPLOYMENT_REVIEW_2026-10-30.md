# Deployment and Infrastructure Security Review (2026-10-30)

## Scope

This document contains deployment, infrastructure, host-tooling, and operational findings separated from the [application code review](SECURITY_AUDIT_2026-10-26.md). Configuration and scripts under `deploy/`, Compose, database grants, network policies, secrets/backups, and release verification belong here, even when addressing them requires editing a deployment script. Application-code acceptance and deployment acceptance are separate.

The source/configuration review used baseline commit `9f6ddee8055e0e311eb9fdcd399c01ca29f5d0ab`. No production configuration, service, network, database role, migration, or secret was changed. No fresh production requests or external network tests were performed. Historical observations below are explicitly attributed to the existing host preflight. This document is not a current operational attestation or permission to expose the service.

**Deployment decision: NO-GO until the following host/release gates and the separate code requirements are satisfied.** Cloudflare Access must not be removed or origin access broadened on the strength of source tests alone.

## Findings

Original audit IDs are retained so existing plans remain traceable.

| ID | Priority / state | Required deployment outcome |
| --- | --- | --- |
| A1 | Blocker: deployed state unresolved | Confirm deployed SHA/enforcement, then deploy and verify a release with app authorization enabled. This is not a missing startup guard in current source. |
| A3 | Blocker: network evidence missing | Prove denial of direct-origin, alternate-hostname, untrusted-LAN, router and IPv6 bypass paths. |
| A11 | High: host-tooling configuration | Restrict or remove the root deployment socket, currently configured as world-writable. |
| A13 | High: database privileges | Separate runtime DML credentials from PostgreSQL initialization/migration administration. |
| A14 | Medium: protection/recovery | Protect keys/secrets/backups and verify a complete isolated restore or explicitly chosen forced-reauthentication recovery. |
| A9 | Conditional High: development topology | Do not expose/tunnel the restored-data mock-auth workflow. Source guards already exist. |
| A7-D | Medium: public response policy | Verify HTTPS, HSTS, proxy trust, cookies, caching and CSP behavior at the real public hostname. |
| A12-D | Release gate | Scan the exact release and its images; earlier local/advisory snapshots do not identify the deployed release. |
| A15-D | Medium: resource policy | Approve explicit rate thresholds, edge/global budgets, private health access and container/host limits. |
| A17-D | Medium: operational controls | Establish administrator MFA, protected audit retention/alerts, incident ownership and recovery. |

## A1. Deployed authorization and release identity

The [previously recorded host preflight](plans/internet-exposure-phase-4-launch-gate.md#preflight-status-2026-10-30) identified deployed SHA `33327386c4351a95f4b7d4f495ffbebd7636be91`, Google sign-in enabled, enforcement disabled, and anonymous HTTP 200 from catalog, admin users, deployment and version. If unchanged, that release violates the logged-in-only requirement. These observations were not repeated during this review.

Current [API startup](../../server/src/MediaDock.Api/Program.cs) already rejects normal Production startup without Google sign-in and `AccessControl:Enforced=true`; the tests passed. [Compose](../../compose.yaml) still supplies disabled defaults, but current source refuses to serve with them. Do not remove the guards to make an unconfigured service start.

**Required:** record the actual deployed SHA/image digest and effective settings. Deploy a tested release with `ASPNETCORE_ENVIRONMENT=Production`, `GOOGLE_AUTH_ENABLED=true`, `ACCESS_CONTROL_ENFORCED=true`, exact HTTPS callback/client configuration and all explicit rate-limit values. Bootstrap the initial administrator from a validated session identity, remove bootstrap values and retain host-only recovery. Prove anonymous API denial on the deployed SHA. Never enable Development open mode for recovery.

## A3. Origin, proxy and edge isolation

[Compose](../../compose.yaml) defaults the API to loopback; the documented deployed topology uses a specific LAN interface and [DOCKER-USER allowlist](../../deploy/firewall/mediadock-next-docker-user.sh). [Tunnel attachment](../../deploy/attach-cloudflared-network.sh) adds a dedicated bridge. These scripts do not prove current network membership or bypass denial. Keep PostgreSQL private and preserve the existing bind/allowlist until any separately approved redesign is verified.

Inspect listeners, container networks, host/upstream firewall, public DNS/alternate hostnames, router forwarding/UPnP and IPv4/IPv6 paths. From outside the LAN, prove direct IP:port and bypass hostname denial; from an untrusted LAN source, prove API-port denial. Repeat after container recreation/redeployment.

[Forwarded-header trust](../../server/src/MediaDock.Api/Authentication/TrustedForwardedHeaders.cs) is off when unset and otherwise trusts one hop from the configured CIDR. Configure only the isolated Tunnel bridge, never wildcard CIDRs or a shared Docker network. Verify sanitized forwarding headers, real client IPs, HTTPS and callback origin through the actual proxy. Restrict production hostnames at the edge and consider a matching `AllowedHosts` value instead of [the wildcard default](../../server/src/MediaDock.Api/appsettings.json).

Cloudflare Access is an additional perimeter, not the MediaDock approval/role system. Test `/`, assets, `/api/*` and `/signin-oidc` without broad auth-bypass rules. A single application Google login may be the eventual public experience only after both code and operational criteria pass; retaining Access may intentionally add another challenge.

## A11. Root deployment-control socket

The [bridge](../../deploy/deploy-control.py) applies socket mode `0666`; its root [systemd service](../../deploy/systemd/mediadock-next-deploy-control.service) creates a `0755` directory. A local process able to reach it can bypass app administrator/CSRF checks, read deployment status/log excerpts and request fixed deployment/retry actions. The read-only bind mount does not prevent socket connections. This is not a demonstrated arbitrary-shell-command vulnerability: actions and subprocess arguments are fixed.

**Required:** use a dedicated caller group/ACL, restrictive directory permissions and socket mode `0660`; consider peer credentials. Disable the bridge/mount if browser deployment is unnecessary. Prove an unrelated host UID cannot read status, retry gates or start deployment, while the intended identity still works. Inspect actual container mounts. Restrict trusted release writers and Docker access, which is root-equivalent. Application actor-event improvements are tracked separately in code finding A17.

## A13. Runtime database privileges

[Compose](../../compose.yaml) reuses `POSTGRES_USER` for PostgreSQL initialization, API, migration and bootstrap. On a freshly initialized official image that account is a superuser; production role flags were not queried. Existing volumes may differ, but the configuration does not separate API DML from migration ownership.

**Required:** inspect effective role flags/grants without exposing credentials. Provision a non-superuser runtime login with only required table/sequence/function privileges; reserve schema ownership/migrations and bootstrap for separately controlled credentials. Remove role/database-creation privileges, grant future objects deliberately and test requests/background jobs with the restricted runtime role. Prove privileged DDL/administration is denied. Existing volumes require a reviewed role rollout, not just changing `POSTGRES_USER`.

## A14. Keys, secrets and complete recovery

Data Protection keys are persisted in a separate [Compose volume](../../compose.yaml), with fixed application name and a non-root [image directory](../../server/Dockerfile) initialized to mode `0700`. Persistence is implemented. The application does not encrypt XML keys at rest; the isolated code-audit probe emitted the expected warning. Treat the key ring as a signing secret and protect storage and copies.

The [backup script](../../deploy/backup.sh) creates a root-only PostgreSQL dump and checks its archive listing. It does not copy Data Protection keys/host secrets or perform a full restore. A separate protected host backup may cover them, but none was inspected. The OMDb key is plaintext in PostgreSQL/dumps; Google/database/SMTP credentials reside in host configuration. A database-only restore is not complete application recovery.

**Required:** document ownership, permissions and encryption for keys, `.env`, dumps and off-host copies. Provide protected key/secret backup or explicitly choose forced reauthentication after recovery. Restore-test data and the chosen session behavior in isolation. Keep production/development key rings separate, rotate exposed secrets, remove bootstrap values and redact cookies/tokens/provider query strings from logs. A future managed-secret/encrypted-provider-key design is optional hardening, not evidence that operational secret protection already exists.

## A9. Restored-data development exposure

[Vite](../../web/vite.config.ts) is loopback-only and rejects non-loopback overrides. Mock authentication rejects non-Development use, non-loopback databases and simultaneous Google sign-in. These source guards are implemented and tested; this is not an outstanding normal-Production authentication bypass.

A separate tunnel/reverse proxy can still expose a loopback listener and appear local to the API. Never tunnel/port-forward the [restored-data workflow](../../scripts/run_local_restored.ps1), never use it as the internet service, and verify ports 5173/5280 are unreachable from untrusted interfaces. Use sanitized data or real auth for sharing, and replace restored production secrets before enabling jobs. Remote reachability of the current development machine was not tested.

## Public Runtime and Monitoring

**A7-D:** force HTTPS at the public edge, verify the exact Google callback and cookie flags, then enable/verify HSTS. Exercise Google login, linking, approval and uploads through the real proxy before CSP enforcement. Prevent edge caching of all API/auth responses, including errors. Code-level CSP and HTML cache improvements are in the separate review.

**A15-D:** choose explicit rate thresholds from expected traffic; verify trusted client-IP attribution and shared-IP fairness. Add edge/global budgets, keep `/health/live` and `/health/ready` internal or edge-denied, and bound container memory/CPU/PIDs and request duration. Application route/concurrency coverage is tracked as code finding A15; neither layer substitutes for the other.

**A17-D:** require strong Google/edge MFA, preferably passkeys, for administrators and protect recovery accounts. Retain redacted, access-controlled audit logs and alerts for approvals, role/status changes, edits, imports, deployments, repeated denials and abnormal costs. Assign incident/recovery owners; never restore access by enabling open mode. Session-revocation implementation is a separate application concern.

## Prior Verification and Release Gates

These are results from the earlier audit on 2026-10-30, not new runs performed while splitting the documents:

- `python -B scripts/run_tests.py deploy`: **3 tests passed**. They do not prove safe installed socket permissions.
- Docker Scout `1.26.0` indexed 158 packages in local `mediadock-phase3-validation:latest`, image ID `0644f4948f68`: no high/critical package findings. This image was not established as the audited commit or deployed release.
- A tracked-file search found no selected private-key/common-token signatures; values were suppressed. Gitleaks was unavailable. History, ignored files, host secrets and arbitrary secret formats were not scanned.
- Prior test logs and npm/NuGet/image reports are under ignored `.test-tmp/`. Application test and dependency findings are retained in the code review. No live target was scanned.

**A12-D required:** scan dependencies, full secret history, container/base-OS packages and provenance against the exact clean release SHA/image digest. The [deployment gate](../../deploy/test.sh) tests/builds trusted source but does not perform those advisory scans. Review/pin image digests with an explicit refresh policy. Its Linux full-release gate was not run on this Windows workstation; it remains a release requirement.

Follow the [master plan](plans/internet-exposure-hardening.md), [host checklist](plans/internet-exposure-phase-3-runtime-host.md) and [launch gate](plans/internet-exposure-phase-4-launch-gate.md). Production deployments, role changes, migrations, network/firewall changes and secret rotation require separate operator approval.

## Acceptance

Record dated evidence tied to the deployed SHA/digest for the real Google browser journey, identity/role/owner matrix, cookie CSRF, size/rate bounds, caching, network bypass denial, restart/redeploy behavior and isolated backup restore. Confirm the separate code requirements, including usable anonymous login delivery and independently protected APIs.

Current deployed settings, Cloudflare/TLS/DNS coverage, origin/router/IPv6 exposure, host/kernel/container-runtime patch status, installed socket/key permissions, role grants, migration state and backup restorability remain unverified. Historical host notes, component tests and an older image scan cannot satisfy these gates.