# API: Catalogs and Awards

Task router for the shared title catalog, Oscars, Golden Globes, and award-recognition lookup. Shared paging, errors, and access modes are in [API contracts](API_CONTRACTS.md); cross-layer flow is in [Architecture](ARCHITECTURE.md).

## Client-visible rules

- When access enforcement is enabled, catalog and award reads require an active linked user or administrator; manual Golden Globes IMDb linking requires an administrator. Open mode makes these routes unprotected, so keep the API within its configured loopback or trusted-network boundary. See [access modes](API_CONTRACTS.md#access-modes).
- The shared title catalog contains occurrence-backed titles. Oscar-only films appear in the Oscar catalog and remain accessible through title details.
- Award recognition lookup combines Oscar and Golden Globes records by IMDb ID; it does not perform title-only association or provider lookup.
- A manual Golden Globes IMDb link is pinned to its nomination group and remains selected after provider failure. Clearing the link returns the group to automatic matching. Automatic matching is type-strict; manual links can retain an IMDb title whose media type differs from the nomination type.

## Authoritative source and tests

- Endpoints: [CatalogEndpoints.cs](../../server/src/MediaDock.Api/Catalog/CatalogEndpoints.cs), [OscarEndpoints.cs](../../server/src/MediaDock.Api/OscarAwards/OscarEndpoints.cs), [GoldenGlobeEndpoints.cs](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeEndpoints.cs), and [MovieAwardsEndpoints.cs](../../server/src/MediaDock.Api/Awards/MovieAwardsEndpoints.cs).
- Contracts: [CatalogContracts.cs](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs), [OscarContracts.cs](../../server/src/MediaDock.Api/OscarAwards/OscarContracts.cs), and [GoldenGlobeContracts.cs](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeContracts.cs).
- API tests: [CatalogEndpointsApiTests.cs](../../server/tests/MediaDock.IntegrationTests/CatalogEndpointsApiTests.cs), [OscarApiTests.cs](../../server/tests/MediaDock.IntegrationTests/OscarApiTests.cs), [GoldenGlobeApiTests.cs](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs), and [MovieAwardsApiTests.cs](../../server/tests/MediaDock.IntegrationTests/MovieAwardsApiTests.cs).