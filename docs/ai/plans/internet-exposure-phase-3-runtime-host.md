# Internet Exposure Phase 3: Runtime and Host

## Goal

Keep the public path HTTPS-only, close origin bypasses, add browser/cache protections, and prevent development or host tooling from widening access.

## Tasks

- Inspect the actual deployed SHA, Compose port bindings, Docker networks, Cloudflare Tunnel and Access policies, public DNS/hostnames, host firewall, and router forwarding. Do not infer live state from repository configuration.
- Restrict the API origin to the dedicated Tunnel and explicitly approved operator paths. Retain the specific-interface bind and source allowlist if LAN access remains; do not publish PostgreSQL. Trust only `X-Forwarded-Proto` from the dedicated proxy CIDR and one hop.
- Configure and verify HSTS after confirming HTTPS coverage. Trial CSP in report-only mode before enforcement; add frame, MIME-sniffing, referrer, and permissions policies. Set private session/API responses to `no-store`; ensure public caches cannot store user data.
- Verify ASP.NET Data Protection key-ring location, persistence, encryption, permissions, and behavior across restart/recreation. Review `.env`, OMDb key and SQL backups, host credentials, deploy socket access, database privileges, logs, backup protection, image pinning/scanning, and patch cadence.
- Keep the restored-data workflow local-only: bind Vite explicitly to loopback, retain the API's loopback binding, prohibit tunneling/port-forwarding while mock auth is enabled, and prefer sanitized data for shared development. Test or document the proxy boundary; a proxy makes the API's immediate peer local.
- Verify outbound URLs displayed from feed data use only approved schemes and cannot introduce scriptable links.

## Verification

- Use host-side read-only inspection first; changes to production networks, firewall, secrets, images, or services require separate operator approval.
- From an external network, confirm direct origin IP/port and alternate hostnames are denied. From an untrusted LAN source, confirm the API port is denied. Confirm only intended routes pass Cloudflare Access and the OIDC callback remains functional.
- Inspect responses for HTTPS, security headers, cache behavior, cookie flags, and absence of public version/data endpoints. Test CSP in a real browser before enforcement.
- Verify ports 5173 and 5280 are not reachable from untrusted interfaces during restored-data development. Confirm Production rejects local mock authentication.
- Perform a controlled backup restore and verify Data Protection keys and application startup behavior without printing secrets or production data.

## Exit Criteria

- No unapproved direct-origin, router, hostname, or LAN bypass exists; PostgreSQL remains private.
- HTTPS, proxy trust, headers, and private cache behavior match the approved design and pass browser/OIDC checks.
- Secrets, key material, images, host services, backups, and deploy controls have documented ownership and protection.
- The mock-auth development workflow is restricted to loopback and cannot be mistaken for an internet-facing login mode.