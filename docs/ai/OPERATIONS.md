# Operations

This page records rollout safeguards, not command sequences. The [local runbook](../../README.md) is canonical for Compose, backup, and restore procedures; the [systemd runbook](../../deploy/systemd/README.md) covers host scheduling, deployment, backups, and firewall units. Verify production state on the host; repository source and documentation do not prove what is deployed.

## Migrations

Normal API startup does not apply EF Core migrations. Run the explicit Compose `migrate` service in the documented order before starting the API after a schema change. `/health/ready` checks database connectivity, not whether the expected migration has been applied.

The per-user ownership migration is data-sensitive. Its effects for zero, one, or multiple existing users and its rollback limitation are documented in [data contracts](DATA_CONTRACTS.md#migration-and-rollout-cautions). Require separate operator approval for its data outcome before applying it outside disposable or staging databases. Passing tests does not authorize a production migration or deployment.

The source-owned identity cleanup is also destructive: `RemoveLegacyTitleIdentity` drops the transitional catalog, award, and favorite tables after reporting remaining fallback metadata and legacy favorite counts. Validate a fresh backup and the expected staging counts, then obtain separate operator approval before applying it. Its `Down` recreates schema only and cannot restore deleted rows; recovery requires the pre-migration backup.

`DeduplicateTorrentUrls` is destructive: it canonicalizes Rutracker topic URLs, keeps only the most recently observed release per URL, removes item states for discarded releases, and deletes catalog rows left without releases. It retains parse logs and source configuration. Validate a fresh backup and staging counts, then obtain separate operator approval before applying it outside disposable databases. Its `Down` cannot restore deleted rows; recovery requires the pre-migration backup.

For job routes and lifecycle contracts, see [operations and jobs](API_OPERATIONS_AND_JOBS.md); scheduler and persistence behavior is in [architecture](ARCHITECTURE.md) and [data behavior](DATA_BEHAVIOR.md).

## IMDb Ratings Refresh

In the administrator awards ingestion panel, choose **Refresh IMDb ratings** to queue a manual run without a file. **View latest IMDb refresh** opens the active or latest completed run, including progress events, result counts, stage durations, and failure details. The API equivalents are the administrator-only POST and latest-job GET documented in [operations and jobs](API_OPERATIONS_AND_JOBS.md). A conflict means another refresh is active; there is no cooldown after it finishes.

After the approved deployment and migrations, verify the deployed API version and health, then inspect the target database's `__EFMigrationsHistory` for `20261111122531_AllowScheduledImdbRatingRefresh` and `background_scheduler_state` for `schedule_name = 'imdb_rating_refresh'`. The API-hosted dispatcher must be running; the checkpoint is created on its first scheduler tick and initially records the latest past slot without queuing it. Inspect the latest refresh and `background_jobs` for a `schedule` job with the expected Wednesday 03:00 `Europe/Sofia` `scheduled_slot_utc`, then inspect its result and events after the first actual run. A checkpoint, repository configuration, or readiness response alone does not prove that the deployed scheduler has run successfully. Do not trigger a production run or apply a migration without separate operator approval.