# Internet Exposure Phase 3: Runtime and Host

## Goal

Keep the public path HTTPS-only, close origin bypasses, add browser/cache protections, and prevent development or host tooling from widening access.

This phase implements/verifies the [deployment and infrastructure findings](../SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md). Application changes and their local regression criteria remain in the separate [code review](../SECURITY_AUDIT_2026-10-26.md); host checks do not replace those fixes.

## Remaining Checks (2026-10-30)

- Record the exact deployed SHA and image digest; inspect effective Compose bindings, Docker networks, Tunnel and Access policies, DNS/hostnames, IPv4/IPv6 firewall, and router forwarding/UPnP. Repository configuration and prior preflight notes are not current live-state evidence.
- From an external network, verify direct-origin and alternate-host denial. From an untrusted LAN source, verify the API port is denied. Confirm only approved routes pass Cloudflare Access, the Google OIDC callback works, and public health access is denied.
- In a browser, verify HTTPS, HSTS, report-only CSP behavior, security headers, cookie flags, private-response `no-store`, and absence of public version/data endpoints. Do not enforce CSP until compatibility is checked.
- Verify Data Protection key persistence, encryption, permissions, and behavior after restart/recreation. The database backup script does not copy the key volume or host secrets; establish a separate protected backup or explicitly choose forced reauthentication after recovery. Perform an isolated data/key recovery drill without printing secrets or production data.
- Restrict or disable the world-writable root deployment-control socket: use a dedicated caller identity/group and prove an unrelated host UID cannot read status, retry gates, or start deployment. A read-only directory mount does not prevent socket connections.
- Inspect PostgreSQL role flags and grants; separate the API runtime role from initialization/migration administration and test the restricted role with requests and background jobs.
- Review host secret protection, administrator MFA/recovery, privileged audit/alerting, image provenance/scanning, full-history secret scanning, patch cadence, and container resource limits. Recheck npm development advisories and scan the exact release image; the audit's local validation-image scan does not identify the release.
- During restored-data development, verify ports 5173 and 5280 are unreachable from untrusted interfaces and Production rejects local mock authentication.
- Use read-only host inspection first. Obtain separate operator approval before changing production networks, firewall rules, secrets, images, or services.

## Exit Criteria

- No unapproved direct-origin, router, hostname, or LAN bypass exists; PostgreSQL remains private.
- HTTPS, proxy trust, headers, and private cache behavior match the approved design and pass browser/OIDC checks.
- Secrets, key material, images, host services, backups, and deploy controls have documented ownership and protection.
- The mock-auth development workflow is restricted to loopback and cannot be mistaken for an internet-facing login mode.