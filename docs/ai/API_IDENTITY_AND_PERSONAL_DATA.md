# API: Identity and Personal Data

Task router for Google sign-in, account linking and administration, favorites, and personal IMDb ratings. Shared paging, errors, and access modes are in [API contracts](API_CONTRACTS.md); cross-layer flow is in [Architecture](ARCHITECTURE.md).

## Client-visible and security rules

- Google identity and email are trusted only after server validation. Linking and registration use validated identity claims, not client-supplied account data; sign-in accepts any Google account with a verified email and has no domain allowlist. Registration requires an explicit user action.
- The API resolves the provider identity to the current account status and role in the database. Bootstrap is a host-only Compose operation; its operator must use identity values from an authenticated session response because the bootstrap tool does not validate Google claims. See the [local operations runbook](../../README.md).
- Favorites, ratings, imports, and personal-rating enrichment always require an active linked user and are scoped to that user, even when general access enforcement is disabled. Client-supplied owner IDs are ignored; administrators see only their own personal data. Open mode does not make personal routes public. See [access modes](API_CONTRACTS.md#access-modes).
- Favorites use IMDb identity, not a catalog or award-source row ID. `POST /api/favorites` accepts `imdbId` and `from`; it succeeds only for an existing enriched IMDb movie. Repeated adds from Oscar and catalog views merge into the same `(user_id, imdb_id)` favorite. `PATCH` and `DELETE /api/favorites/{imdbId}` use the same owner-scoped key.
- `ToDownload` is a future-search marker and does not require an existing torrent release. Unmatched records and IMDb rows whose metadata is pending or unsuccessful cannot be added; the web controls are only offered for enriched movie matches.
- Personal-rating import is an owner-scoped merge, not a replacement: IDs omitted from a later file remain stored. The JSON is processed in memory, not saved as an uploaded file.
- `GET /api/recommendations` always requires an active linked user, including when access enforcement is disabled. The server resolves the owner from validated identity; it does not accept a client-supplied user ID. Results exclude that user's rated and `ToWatch` movies, but include `ToDownload`-only movies.
- `GET` and `PUT /api/settings/recommendations` and `POST /api/settings/recommendations/recalculate-votes` use the administrator policy when enforcement is enabled. They manage global settings, not user preferences; open mode leaves them unprotected and does not change the configured network boundary.

## Recommendation Settings

- `GET /api/settings/recommendations` returns the global median period, vote-recalculation dirty flag, last-success timestamp and period, and the configured source-qualified win and nomination points.
- `PUT /api/settings/recommendations` accepts `medianPeriodYears` and the award-weight rows. The period must be positive; award rows must reference existing unique source/category pairs and use nonnegative points. Changing the period marks vote weights dirty.
- `POST /api/settings/recommendations/recalculate-votes` currently recalculates synchronously and returns the calculation time, duration, period, and movie/vote counts on success. The API has a 10-second execution limit; a 504 timeout leaves the dirty flag set and the web warning visible.
- The settings page displays “New metadata detected. Recommended scores may be inaccurate. Recalculate vote weights.” while the dirty flag is true. If recalculation later returns `202 Accepted`, the page polls settings until the flag is false; the current endpoint returns `200 OK` for synchronous completion.

## Personal Recommendations

- `GET /api/recommendations` uses the shared `PageResponse<T>` contract: `page` defaults to `1`, `pageSize` defaults to `25`, and the maximum page size is `100`.
- Optional inclusive `yearFrom`/`yearTo` and repeatable `genres` query values filter candidates before counting and paging. A movie matches any selected genre; the response's year bounds describe the active user's eligible candidates before these filters.
- `GET /api/recommendations/genres` returns sorted distinct genre values from all `imdb_titles` rows and requires an active linked user under the recommendation access policy.
- Every request must provide `ratingWeight`, `votesWeight`, and `awardsWeight`. Each is in `[0,1]` and their decimal sum must equal `1.0`. The web client's first-use values are `0.50`, `0.25`, and `0.25`, respectively; they remain in client/request state and are not persisted by the API.
- The recommendations page uses three integer-percent sliders, redistributes the remainder when one changes, and sends coefficients totaling exactly `1.0` with every paged request. Changing pages keeps the current coefficients and filters; changing a filter resets the page to `1`, and reopening the view starts from the approved defaults.
- Candidates are enriched canonical IMDb movie rows with a non-empty title, a non-null IMDb rating, and at least one Oscar or qualifying Golden Globes nomination linked by IMDb ID. Golden Globes links require both nominee type and canonical IMDb media type `movie`. Torrent-only, unmatched, non-movie, pending, incomplete-title, and missing-rating records are excluded.
- The rating component uses a Bayesian prior: the prior mean is the average IMDb rating across enriched IMDb movie rows with a rating, and the prior strength is `1,000` raw IMDb votes. A missing vote count uses the prior mean. Popularity is the capped normalized vote weight; award points use the configured source-qualified win or nomination value multiplied by winner-derived category taste, with an award-component cap of `10.0`.
- Results sort by final score descending, then IMDb ID ascending. Each item includes title data, normalized score components, applied coefficients, final score, and distinct contributing award events with source, category, outcome, base points, taste multiplier, and adjusted points. Award events expose Oscar ceremony numbers separately from Golden Globes ceremony years.
- The web renders the result page and score/event breakdown returned by the API; it does not rescore candidates, fetch catalog rows for recommendation candidates, or persist the coefficients.

## Authoritative source and tests

- Endpoints: [AuthenticationEndpoints.cs](../../server/src/MediaDock.Api/Authentication/AuthenticationEndpoints.cs), [AccessControl.cs](../../server/src/MediaDock.Api/Authentication/AccessControl.cs), [UserAdministrationEndpoints.cs](../../server/src/MediaDock.Api/Administration/UserAdministrationEndpoints.cs), [FavoriteEndpoints.cs](../../server/src/MediaDock.Api/Favorites/FavoriteEndpoints.cs), and [PersonalRatingsEndpoints.cs](../../server/src/MediaDock.Api/PersonalRatings/PersonalRatingsEndpoints.cs).
- API tests: [GoogleAuthenticationApiTests.cs](../../server/tests/MediaDock.IntegrationTests/GoogleAuthenticationApiTests.cs) and [PersonalRatingsApiTests.cs](../../server/tests/MediaDock.IntegrationTests/PersonalRatingsApiTests.cs).