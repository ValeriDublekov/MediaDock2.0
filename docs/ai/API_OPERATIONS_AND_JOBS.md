# API: Operations and Jobs

Task router for health/version, application configuration, deployment, operational history, ingestion, and background jobs. Shared paging, errors, and access modes are in [API contracts](API_CONTRACTS.md); cross-layer flows are in [Architecture](ARCHITECTURE.md).

## Client-visible and operator rules

- With access enforcement enabled, configuration, operational history, background jobs, and deployment require an active administrator. Open mode makes these routes unprotected; keep them within the configured loopback or trusted-network boundary. See [access modes](API_CONTRACTS.md#access-modes).
- Deployment control proxies only a fixed request set to the host service. Clients cannot supply commands, paths, or unit names, and the endpoints must remain within the trusted LAN boundary. Retrying a failed deployment gate never clears the separate deployment-failure marker.
- Provider settings never return the saved OMDb key. Usage and job projections expose bounded/safe operational data, not credentials or provider request URLs.
- Background work is asynchronous; clients follow the returned job identity to read status and events. Personal IMDb-title enrichment is owner-scoped, and global job status/events do not expose personal rating IDs or scores.
- RSS scans do not run award-catalog imports or enrichment as follow-up operations.

## Authoritative source and tests

- Endpoints: [HealthEndpoints.cs](../../server/src/MediaDock.Api/Health/HealthEndpoints.cs), [VersionEndpoints.cs](../../server/src/MediaDock.Api/Versioning/VersionEndpoints.cs), [SourceSettingsEndpoints.cs](../../server/src/MediaDock.Api/Sources/SourceSettingsEndpoints.cs), [OperationalHistoryEndpoints.cs](../../server/src/MediaDock.Api/Operations/OperationalHistoryEndpoints.cs), [DeploymentControlEndpoints.cs](../../server/src/MediaDock.Api/Operations/DeploymentControlEndpoints.cs), and [BackgroundJobEndpoints.cs](../../server/src/MediaDock.Api/BackgroundJobs/BackgroundJobEndpoints.cs).
- Contracts: [SourceSettingsContracts.cs](../../server/src/MediaDock.Api/Sources/SourceSettingsContracts.cs) and [OperationsContracts.cs](../../server/src/MediaDock.Api/Operations/OperationsContracts.cs).
- API tests: [CatalogApiTests.cs](../../server/tests/MediaDock.IntegrationTests/CatalogApiTests.cs), [BackgroundJobApiTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobApiTests.cs), [BackgroundJobExecutionTests.cs](../../server/tests/MediaDock.IntegrationTests/BackgroundJobExecutionTests.cs), and [DeploymentControlApiTests.cs](../../server/tests/MediaDock.IntegrationTests/DeploymentControlApiTests.cs).