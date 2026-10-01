# Host Schedule

## Current Production Deployment (2026-09-30)

The standalone production checkout is `/opt/docker/projects/mediadock-next`,
first deployed from GitHub `main` commit `2dd39927d5a0b02d74c9f7f8e3212a8e74c731c8`.
Read `/var/lib/mediadock-deploy/deploy-state` for the live deployed SHA. The legacy
monorepo checkout is retained at
`/opt/docker/projects/mediadock-next-legacy-20260930` for rollback. Production
uses PostgreSQL database `mediadock2` with baseline migration
`20260930122500_InitialRelationalSchema`; the original `mediadock` database and
its six migrations remain intact. The deployment backup
`daily-20260930T150707Z.dump` passed `pg_restore -l` and is present in Restic.
The UI is available to trusted LAN clients at
`http://<server-LAN-IPv4>:8081/`; the actual host address is stored only in
server configuration. The app has no login. PostgreSQL remains bound to
`127.0.0.1:5432`.

- `mediadock-next-deploy.timer` is enabled and checks the new GitHub `main`
	every five minutes after each check finishes; the first post-cutover poll
	completed as a no-op on the deployed commit.
- `mediadock-next-backup.timer` is enabled for 03:00 UTC; the existing Restic timer starts at about 03:30 UTC with up to 15 minutes of random delay.
- The deploy dump is root-only and has been confirmed in the latest Restic snapshot.
- This host still runs the pre-background-jobs release. The legacy Worker service is installed, its timer is not installed or enabled, and no scan has run. The repository's API-hosted ingestion migrations have not been deployed.
- LAN readiness/UI/catalog checks returned HTTP 200. Router port-forward and non-LAN denial checks remain unverified.

The installation instructions below describe how to provision or operate the units; this status block records the verified production state.

## LAN API Firewall

The optional `mediadock-next-firewall.service` reads `/etc/default/mediadock-next-firewall`. Create that root-owned host file with `APP_BIND_ADDRESS`, `APP_PORT`, and `TRUSTED_LAN_CIDR` before enabling the unit; use a specific IPv4 bind and the intended trusted subnet. Keep the real host address and subnet out of Git. The unit limits filtering to the configured API destination and must not be treated as authentication.

These host-side systemd units are for a single Ubuntu host. The production
Compose project is installed at `/opt/docker/projects/mediadock-next` and
uses its ignored `.env` file. The `mediadock` OS account must be able to access
the Docker socket for the deployment gate and Testcontainers; membership in the
`docker` group grants root-equivalent host access.

## Database dump

Install the root-only PostgreSQL dump script and its timer before the existing
Restic timer. The dump runs at 03:00 UTC, writes custom-format archives under
`/opt/docker/backups/mediadock-next`, validates each archive with `pg_restore -l`,
and keeps the newest 14 daily dumps. The existing Restic backup already covers
`/opt/docker`; it starts at 03:30 UTC with its configured randomized delay. Both
jobs use `/run/lock/homeserver-restic.lock` so they do not overlap.

```sh
sudo install -o root -g root -m 0750 deploy/backup.sh /usr/local/sbin/mediadock-next-backup
sudo install -o root -g root -m 0644 deploy/systemd/mediadock-next-backup.service /etc/systemd/system/mediadock-next-backup.service
sudo install -o root -g root -m 0644 deploy/systemd/mediadock-next-backup.timer /etc/systemd/system/mediadock-next-backup.timer
sudo systemctl daemon-reload
sudo systemctl enable --now mediadock-next-backup.timer
```

Run one dump manually before accepting the schedule:

```sh
sudo systemctl start mediadock-next-backup.service
sudo systemctl status --no-pager mediadock-next-backup.service
```

The dump contains the database-stored OMDb key. Keep the backup directory
root-only and never print the archive or the production `.env`.

## One-Time Baseline Reset (Per Environment)

The schema now has one baseline migration, `20260930122500_InitialRelationalSchema`, replacing the previous six-migration chain. An existing database whose `__EFMigrationsHistory` contains the old migration IDs cannot apply this baseline in place: EF will consider it pending and its table creation will collide with the existing schema. This is an explicit data/schema reset, not a normal deployment migration. The deployment script does not delete the Compose `postgres_data` volume.

Perform this procedure separately for each environment, only after its owner approves the data disposition:

1. Confirm that no required data will be lost or that required data has a reviewed export/import path. Record the target environment and approval; do not infer approval from a successful build or deployment gate.
2. Create a final custom-format PostgreSQL dump with the root-only backup service. Validate it with `pg_restore -l` and verify restoreability in an isolated database. The dump contains the plaintext OMDb key and must remain root-only.
3. Disable that environment's deployment timer, stop active deploy/API writers, and explicitly disable/stop any legacy Worker timer/service before the schema operation.
4. Use the separately approved database-administration procedure for that environment to provision an empty database or fresh volume. Retain the old volume and verified dump until application checks pass. Do not use `docker compose down -v` as routine cleanup and do not add volume deletion to `deploy.sh`.
5. Start PostgreSQL and select the validated API image from the release containing the new baseline. Confirm the ignored `.env` selects the approved empty target database (for this production cutover, `POSTGRES_DB=mediadock2`), not the preserved legacy database. Never rely on Compose's default `mediadock-next-api` tag: it may still point to an older release. Replace `YOUR_VALIDATED_RELEASE_SHA` with the full validated release SHA and run the one-shot migration profile from the Compose project directory with that image pinned:

	```sh
	cd /opt/docker/projects/mediadock-next
	release_sha=YOUR_VALIDATED_RELEASE_SHA
	docker image inspect "mediadock-next-api:$release_sha" >/dev/null
	API_IMAGE="mediadock-next-api:$release_sha" docker compose --project-name mediadock-next --project-directory "$PWD" --env-file "$PWD/.env" --file "$PWD/compose.yaml" --profile tools run --rm --no-deps migrate
	```

6. Verify that `__EFMigrationsHistory` contains the approved migrations, then start the API and check `/health/ready`, `/api/catalog`, and `/api/oscars`. Confirm provider settings use singleton `id = 1` and the scheduler checkpoint is initialized without an immediate historical catch-up.
7. Keep the verified dump and old volume until API checks and an explicitly approved manual job succeed. Scan scheduling is now API-hosted; do not reinstall a Worker service or timer.

Never point the one-shot migration profile at the old schema as a substitute for this procedure. The 2026-09-30 production cutover used a separate empty `mediadock2` database with explicit approval; the original `mediadock` database was not reset. Any future reset requires its own environment-specific approval.

## Automated deployment

The deployment unit fetches only the exact public GitHub repository
`https://github.com/ValeriDublekov/MediaDock2.0.git` and only its `main` branch.
It creates a clean worktree without `.env`, runs `deploy/test.sh`, builds
images tagged with the full commit SHA, and embeds `YYYY.MM.DD+<7-character-SHA>`
from the commit's UTC committer date. After readiness, it verifies `/api/version`
reports the exact target SHA and version before recording success. Python 3 is
required on the host to parse that response. It creates and validates a database dump
before the migration command, and checks `/health/ready` plus the configured
API bind after startup. It records the deployed SHA in
`/var/lib/mediadock-deploy`; that directory is root-owned and group-readable by
the deployment account, while its state files remain root-managed. Gate and
deployment logs under `/var/log` are root-only.

After a successful deployment, the runner refreshes its root-owned installed
copy from the validated staging worktree, so future changes to `deploy.sh` take
effect automatically. Existing hosts need this one-time runner refresh because
an older installed copy cannot update itself. From the production checkout:

```sh
sudo install -o root -g root -m 0750 deploy/deploy.sh /usr/local/sbin/mediadock-next-deploy
```

After a new version passes the staging gate, database backup, migration, and
readiness check, the deploy script can send a success email over SMTP. A run
with no new commit sends no email. Configure the non-secret settings in
`/etc/default/mediadock-next-deploy`:

```ini
DEPLOY_NOTIFY_TO=recipient@example.net
DEPLOY_NOTIFY_FROM=mediadock@example.net
DEPLOY_SMTP_URL=smtps://smtp.provider.example:465
DEPLOY_SMTP_NETRC_FILE=/etc/mediadock-next-deploy.smtp.netrc
```

The sender must be accepted by the relay. Create the credential file with
`sudo install -o root -g root -m 0600 /dev/null /etc/mediadock-next-deploy.smtp.netrc`,
then edit it with `sudoedit`. Its `machine` value must match the SMTP URL host:

```text
machine smtp.provider.example
login smtp-account
password smtp-app-password
```

Keep real credentials out of Git. The deploy script requires the netrc file to
be a regular root-owned file with mode `600`, and requires TLS for SMTP. If
configuration is missing or sending fails, the successful deployment remains
successful; the outcome is recorded in the root-only deployment log. Email
delivery is best-effort and is not retried automatically.

If the clean staging gate fails, the failing commit SHA is recorded in
`/var/lib/mediadock-deploy/gate-failed`; later five-minute polls skip that SHA
instead of repeating the full test suite. A newer `main` commit is tested
normally. To retry the same commit after fixing a transient host issue, clear
the marker and start the service explicitly:

```sh
sudo rm -f /var/lib/mediadock-deploy/gate-failed
sudo systemctl start mediadock-next-deploy.service
```

The deployment runner uses
`/var/lib/mediadock-deploy/mediadock-next-operation.lock` to serialize deployment
operations across reboots. Immediately
before stopping the API, deployment writes `/var/lib/mediadock-deploy/deploy-failed`
with the target SHA, last-good SHA, and exact validated dump path. A failed
migration leaves this marker in place; later deployments refuse to proceed
until an operator restores and verifies the previous state.

For a new host, install the files and host-only bind configuration, but keep
the timer disabled until the clean-main gate and unit validation have passed:

```sh
sudo install -o root -g root -m 0750 deploy/deploy.sh /usr/local/sbin/mediadock-next-deploy
sudo install -o root -g root -m 0644 deploy/systemd/mediadock-next-deploy.service /etc/systemd/system/mediadock-next-deploy.service
sudo install -o root -g root -m 0644 deploy/systemd/mediadock-next-deploy.timer /etc/systemd/system/mediadock-next-deploy.timer
sudo install -o root -g root -m 0600 deploy/systemd/mediadock-next-deploy.env.example /etc/default/mediadock-next-deploy
sudo install -d -o root -g mediadock -m 0750 /var/lib/mediadock-deploy
sudo install -d -o root -g root -m 0700 /var/lib/mediadock-deploy/docker
sudo install -o root -g mediadock -m 0660 /dev/null /var/lib/mediadock-deploy/mediadock-next-operation.lock
sudo systemctl daemon-reload
sudo systemctl is-enabled mediadock-next-deploy.timer || true
```

Replace the placeholder in `/etc/default/mediadock-next-deploy` locally, then
run the service once and inspect its result before enabling the timer:

```sh
sudo systemctl start mediadock-next-deploy.service
sudo systemctl show mediadock-next-deploy.service --property=Result --value
sudo systemctl status --no-pager mediadock-next-deploy.service
```

For a failed migration or health check, disable deployment and any legacy Worker schedules,
preserve the logs, and do not start an older API until the pre-migration schema
has been restored. The deployment log records the dump path; the last successful
`deploy-state` remains unchanged until recovery. The failure marker records the
exact dump path and last-good API SHA. Inspect those values before running the
following recovery commands from Bash:

```sh
sudo systemctl disable --now mediadock-next-deploy.timer
sudo systemctl disable --now mediadock-worker.timer 2>/dev/null || true
sudo systemctl stop mediadock-worker.service 2>/dev/null || true
sudo cat /var/lib/mediadock-deploy/deploy-failed
app_dir=/opt/docker/projects/mediadock-next
dump=$(sudo awk -F= '$1 == "pre_migration_dump" { print $2; exit }' /var/lib/mediadock-deploy/deploy-failed)
previous_sha=$(sudo awk -F= '$1 == "previous_sha" { print $2; exit }' /var/lib/mediadock-deploy/deploy-failed)
compose=(docker compose --project-name mediadock-next --project-directory "$app_dir" --env-file "$app_dir/.env" --file "$app_dir/compose.yaml")
sudo "${compose[@]}" stop api
sudo docker cp "$dump" mediadock-next-db-1:/tmp/mediadock-rollback.dump
sudo docker exec -u 0 mediadock-next-db-1 chmod 0644 /tmp/mediadock-rollback.dump
sudo "${compose[@]}" exec -T db sh -c 'dropdb -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB" && pg_restore --exit-on-error -U "$POSTGRES_USER" -d "$POSTGRES_DB" /tmp/mediadock-rollback.dump'
sudo docker exec -u 0 mediadock-next-db-1 rm -f /tmp/mediadock-rollback.dump
sudo env API_IMAGE="mediadock-next-api:$previous_sha" "${compose[@]}" up -d --no-build api
api_address=$(sudo awk -F= '$1 == "APP_BIND_ADDRESS" { print $2; exit }' /etc/default/mediadock-next-deploy)
api_port=$(sudo awk -F= '$1 == "APP_PORT" { print $2; exit }' /etc/default/mediadock-next-deploy)
curl --fail --silent --show-error "http://$api_address:$api_port/health/ready"
sudo rm -f /var/lib/mediadock-deploy/deploy-failed
```

Confirm the marker values and restore result before removing the marker. Keep
the dump root-only; it contains the database-stored OMDb key. Review database
and API state before re-enabling either schedule.

Only after the manual deployment and rollback materials are reviewed may the
timer be enabled:

```sh
sudo systemctl enable --now mediadock-next-deploy.timer
```

## API-Hosted Ingestion Cutover

The new API release owns the scan schedule: 07:00 and 18:00 in
`Europe/Sofia`, with one coalesced catch-up slot after downtime. Do not install
a Worker wrapper, service, or timer. Before the first deployment of a release
containing background-job migrations, inspect the live `deploy-state`, verify a
fresh database dump and migration state, and confirm whether legacy units are
installed or active. The deployment script also stops/disables those legacy
units after its pre-migration backup and refuses to proceed while either is
active.

On the production host, verify and stop the legacy units before starting the
cutover:

```sh
systemctl is-enabled mediadock-worker.timer || true
systemctl is-active mediadock-worker.service || true
sudo systemctl disable --now mediadock-worker.timer 2>/dev/null || true
sudo systemctl stop mediadock-worker.service 2>/dev/null || true
systemctl is-active mediadock-worker.timer mediadock-worker.service
```

After the backup and migration, verify `/health/ready`, the deployed
`/api/version`, job endpoints, and the scheduler checkpoint. On first scheduler
activation there must be no immediate catch-up for a slot before activation.
Configure the OMDb key/confirmed quota and enabled feeds, then start one
operator-approved manual scan from Configuration. Before declaring rollout
complete, observe a scheduled 07:00 or 18:00 slot and exercise restart recovery
in staging. Keep the backup and rollback image until acceptance passes.

Production rollback remains an operator action: restore the exact validated
pre-migration dump and start the last-good API image using the failure-marker
procedure above. Do not start an older API against a migrated schema.