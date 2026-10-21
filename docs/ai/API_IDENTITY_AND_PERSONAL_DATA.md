# API: Identity and Personal Data

Task router for Google sign-in, account linking and administration, favorites, and personal IMDb ratings. Shared paging, errors, and access modes are in [API contracts](API_CONTRACTS.md); cross-layer flow is in [Architecture](ARCHITECTURE.md).

## Client-visible and security rules

- Google identity and email are trusted only after server validation. Linking and registration use validated identity claims, not client-supplied account data; sign-in accepts any Google account with a verified email and has no domain allowlist. Registration requires an explicit user action.
- The API resolves the provider identity to the current account status and role in the database. Bootstrap is a host-only Compose operation; its operator must use identity values from an authenticated session response because the bootstrap tool does not validate Google claims. See the [local operations runbook](../../README.md).
- Favorites, ratings, imports, and personal-rating enrichment always require an active linked user and are scoped to that user, even when general access enforcement is disabled. Client-supplied owner IDs are ignored; administrators see only their own personal data. Open mode does not make personal routes public. See [access modes](API_CONTRACTS.md#access-modes).
- Favorites use IMDb identity, not a catalog or award-source row ID. `POST /api/favorites` accepts `imdbId` and `from`; it succeeds only for an existing enriched IMDb movie. Repeated adds from Oscar and catalog views merge into the same `(user_id, imdb_id)` favorite. `PATCH` and `DELETE /api/favorites/{imdbId}` use the same owner-scoped key.
- `ToDownload` is a future-search marker and does not require an existing torrent release. Unmatched records and IMDb rows whose metadata is pending or unsuccessful cannot be added; the web controls are only offered for enriched movie matches.
- Personal-rating import is an owner-scoped merge, not a replacement: IDs omitted from a later file remain stored. The JSON is processed in memory, not saved as an uploaded file.

## Authoritative source and tests

- Endpoints: [AuthenticationEndpoints.cs](../../server/src/MediaDock.Api/Authentication/AuthenticationEndpoints.cs), [AccessControl.cs](../../server/src/MediaDock.Api/Authentication/AccessControl.cs), [UserAdministrationEndpoints.cs](../../server/src/MediaDock.Api/Administration/UserAdministrationEndpoints.cs), [FavoriteEndpoints.cs](../../server/src/MediaDock.Api/Favorites/FavoriteEndpoints.cs), and [PersonalRatingsEndpoints.cs](../../server/src/MediaDock.Api/PersonalRatings/PersonalRatingsEndpoints.cs).
- API tests: [GoogleAuthenticationApiTests.cs](../../server/tests/MediaDock.IntegrationTests/GoogleAuthenticationApiTests.cs) and [PersonalRatingsApiTests.cs](../../server/tests/MediaDock.IntegrationTests/PersonalRatingsApiTests.cs).