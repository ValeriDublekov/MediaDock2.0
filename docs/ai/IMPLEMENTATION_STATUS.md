# Implementation Status

This page summarizes capabilities in the repository source and configuration. `Implemented` does not claim a passing test run or production deployment; verify live SHA, migration, backup, and unit state on the host using the [local runbook](../../README.md) and [systemd runbook](../../deploy/systemd/README.md). `Not implemented` and `MVP limitation` are not delivery commitments.

## MVP Matrix

| Area | Status | Scope and authoritative reference |
| --- | --- | --- |
| Web app and local runtime | Implemented | React is bundled into the production API image; Compose runtime is documented in the [local runbook](../../README.md) and [architecture](ARCHITECTURE.md). |
| Catalog | Implemented | Search, filters, paging, and title details are available in the [catalog UI](../../web/src/features/catalog/CatalogView.tsx) and [API contract](API_CATALOGS_AND_AWARDS.md). |
| Favorites | Implemented | Per-user favorite operations are documented in [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md). |
| Sources and matching settings | Implemented | Source profiles and matching configuration are documented in [operations and jobs](API_OPERATIONS_AND_JOBS.md). |
| Provider settings | Implemented | Provider configuration contracts are in [operations and jobs](API_OPERATIONS_AND_JOBS.md); secret handling is in [security and operations](SECURITY_AND_OPERATIONS.md). |
| User registration review | Implemented, limited | Account linking and administration are documented in [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md); access limits are in [security and operations](SECURITY_AND_OPERATIONS.md). |
| Personal IMDb ratings and shared metadata | Implemented | Owner-scoped ratings are documented in [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md); persistence ownership is in [data contracts](DATA_CONTRACTS.md). |
| RSS ingestion and metadata | Implemented | Queue/API behavior is in [operations and jobs](API_OPERATIONS_AND_JOBS.md); persistence behavior is in [data behavior](DATA_BEHAVIOR.md). |
| Oscar CSV import and enrichment | Implemented, manual | Routes and catalog behavior are in [catalogs and awards](API_CATALOGS_AND_AWARDS.md); import and persistence behavior is in [data behavior](DATA_BEHAVIOR.md). |
| Oscar catalog | Implemented | Catalog contracts are in [catalogs and awards](API_CATALOGS_AND_AWARDS.md). |
| Golden Globes catalog, import, and enrichment | Implemented, manual | Routes and catalog behavior are in [catalogs and awards](API_CATALOGS_AND_AWARDS.md); persistence behavior is in [data behavior](DATA_BEHAVIOR.md). |
| Operations history and health | Implemented | Routes and response contracts are in [operations and jobs](API_OPERATIONS_AND_JOBS.md). |
| Persistence and schema | Implemented | Canonical ownership and migration cautions are in [data contracts](DATA_CONTRACTS.md); schema details are in [data schema](DATA_SCHEMA.md). |
| Host scheduling and backup procedure | Implemented | Operator commands are in the [local runbook](../../README.md) and [systemd runbook](../../deploy/systemd/README.md). |

## MVP Limitations

| Area | Status | Limitation and reference |
| --- | --- | --- |
| Authentication and authorization | Partial | Optional sign-in and access enforcement exist; open mode leaves other routes unprotected. See [security and operations](SECURITY_AND_OPERATIONS.md). |
| Feed/provider coverage | MVP limitation | RSS transport is host-restricted and metadata uses OMDb; arbitrary feeds and providers are unsupported. See [operations and jobs](API_OPERATIONS_AND_JOBS.md). |
| Deployment and recovery | MVP limitation | Deployment targets local Compose or a single host; backup and restore are operator actions. See the [local runbook](../../README.md) and [systemd runbook](../../deploy/systemd/README.md). |

No item in the limitations table is implicitly planned. A capability should be labelled `Planned` only when an approved plan explicitly commits to it; until implemented and verified, it is not part of the shipped MVP.