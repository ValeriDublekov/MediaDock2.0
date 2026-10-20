# Persistence And Data Contracts

This page routes PostgreSQL persistence work in `server/`. Entity configurations and migrations define the schema; repositories define persistence behavior. The repository documents intended behavior, not the state of a deployed database. Verify production state on the host.

## Canonical Ownership

- [`imdb_titles`](DATA_SCHEMA.md) is the shared canonical store for provider metadata when an IMDb ID is known. `titles` retains provider-detail fields only as fallback while no IMDb ID is available; a check constraint prevents those fields from coexisting with `titles.imdb_id`. When a title is linked, fallback details are moved into missing shared fields and then cleared. `TitleText`, `Year`, and `MediaType` on `titles` remain catalog identity/display fields. Personal ratings, Oscar films, and Golden Globes nominations retain separate ownership and point to shared IMDb metadata where applicable.
- `titles` represents canonical catalog titles and torrent observations. Oscar and Golden Globes imports do not create torrent occurrences. `favorite_movies` is owner-scoped to a canonical title; `personal_ratings` is owner-scoped to a normalized IMDb ID and has no `titles` foreign key. Personal scores remain separate from shared metadata.
- Each `sources` row represents one RSS URL. Disabled sources are retained because occurrences, parse logs, and item states refer to them.

## Choose A Reference

- [Data schema](DATA_SCHEMA.md): concise entity map, important keys and relationships, database-enforced invariants, and shared IMDb migration effects.
- [Data behavior](DATA_BEHAVIOR.md): transaction boundaries, idempotency, retries, and repository persistence behavior.
- [API contracts](API_CONTRACTS.md) and [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md): HTTP routes, DTOs, validation, and authorization.
- [Architecture](ARCHITECTURE.md): cross-layer persistence and job flows.
- [Testing](TESTING.md): test categories, commands, prerequisites, and migration coverage.

## Migration And Rollout Cautions

- The generated [`InitialRelationalSchema`](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20260930122500_InitialRelationalSchema.cs) baseline replaces the six earlier migrations. A database whose `__EFMigrationsHistory` records those earlier migrations requires an explicit per-environment reset before this baseline can be applied. Follow the [one-time baseline reset procedure](../../deploy/systemd/README.md#one-time-baseline-reset-per-environment); do not infer deployed state from repository source.
- [`AddSharedImdbTitles`](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261016160138_AddSharedImdbTitles.cs) normalizes and consolidates existing IMDb references and metadata without provider requests, and aborts if legacy personal ratings contain blank IMDb IDs. Its data-preservation effects are summarized in [Data schema](DATA_SCHEMA.md).
- [`EnforceTitleMetadataOwnership`](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261020122556_EnforceTitleMetadataOwnership.cs) backfills missing shared provider fields, keeps populated `imdb_titles` values when copies differ, clears provider details from linked `titles`, and enforces that ownership rule. IMDb-less title fallback remains intact.
- [`AddUserOwnershipToFavoritesAndRatings`](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/20261016071457_AddUserOwnershipToFavoritesAndRatings.cs) changes personal data to owner-scoped composite keys: it deletes legacy favorites/ratings when there are no users, assigns them to the only user when exactly one exists, and copies every legacy row to every user when multiple users exist. Its down migration refuses rollback with multiple users. Review these data effects and the identity migration tests before applying it to an existing database.
- Apply migrations only through the documented isolated/staging and deployment procedures. Routine tests use disposable databases and must never be pointed at production data.

For migration details, consult the migration files and [Data schema](DATA_SCHEMA.md). For operational deployment state and safety gates, use [operations](OPERATIONS.md) and the [local runbook](../../README.md).
