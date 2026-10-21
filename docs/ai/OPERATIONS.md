# Operations

This page records rollout safeguards, not command sequences. The [local runbook](../../README.md) is canonical for Compose, backup, and restore procedures; the [systemd runbook](../../deploy/systemd/README.md) covers host scheduling, deployment, backups, and firewall units. Verify production state on the host; repository source and documentation do not prove what is deployed.

## Migrations

Normal API startup does not apply EF Core migrations. Run the explicit Compose `migrate` service in the documented order before starting the API after a schema change. `/health/ready` checks database connectivity, not whether the expected migration has been applied.

The per-user ownership migration is data-sensitive. Its effects for zero, one, or multiple existing users and its rollback limitation are documented in [data contracts](DATA_CONTRACTS.md#migration-and-rollout-cautions). Require separate operator approval for its data outcome before applying it outside disposable or staging databases. Passing tests does not authorize a production migration or deployment.

The source-owned identity cleanup is also destructive: `RemoveLegacyTitleIdentity` drops the transitional catalog, award, and favorite tables after reporting remaining fallback metadata and legacy favorite counts. Validate a fresh backup and the expected staging counts, then obtain separate operator approval before applying it. Its `Down` recreates schema only and cannot restore deleted rows; recovery requires the pre-migration backup.

For job routes and lifecycle contracts, see [operations and jobs](API_OPERATIONS_AND_JOBS.md); scheduler and persistence behavior is in [architecture](ARCHITECTURE.md) and [data behavior](DATA_BEHAVIOR.md).