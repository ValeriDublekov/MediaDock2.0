# API: Catalogs and Awards

Canonical contracts for the shared title catalog, Oscars, Golden Globes, and award-recognition lookup. Shared paging and error behavior is in [API contracts](API_CONTRACTS.md); cross-layer flow is in [Architecture](ARCHITECTURE.md).

## Routes

With access enforcement enabled, catalog and award reads require an active linked `user` or `admin`. Manual Golden Globes IMDb linking requires an active administrator. In open mode, these routes are unprotected; see [shared access behavior](API_CONTRACTS.md#access-modes).

| Method and route | Request | Success | Other documented outcomes |
| --- | --- | --- | --- |
| `GET /api/catalog` | [CatalogQuery](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs) | `200 PageResponse<CatalogTitleResponse>` with optional catalog bounds | `400 ValidationProblemDetails` for invalid query values. |
| `GET /api/titles/{id:long}` | Route `id` | `200 TitleDetailsResponse` | `404 ProblemDetails` when the title does not exist. |
| `GET /api/titles/{id:long}/occurrences` | Route `id`, [OccurrencesQuery](../../server/src/MediaDock.Api/Catalog/CatalogContracts.cs) | `200 PageResponse<OccurrenceResponse>` | `400 ValidationProblemDetails`; `404 ProblemDetails` when the title does not exist. |
| `GET /api/oscars` | [OscarCatalogQuery](../../server/src/MediaDock.Api/OscarAwards/OscarContracts.cs) | `200 PageResponse<OscarFilmResponse>` with optional Oscar bounds | `400 ValidationProblemDetails` for invalid query values or a reversed film-year range. |
| `GET /api/oscars/categories` | None | `200 string[]` of distinct canonical nomination categories in sorted order | None declared. |
| `GET /api/oscars/{id:long}` | Route `id` | `200 OscarFilmResponse` with all selected-category nominations and linked title metadata | `404 ProblemDetails` when the Oscar film does not exist. |
| `GET /api/titles/{titleId:long}/oscars` | Canonical title ID | `200 OscarFilmResponse[]` for all linked Oscar records and nominations, or an empty array | `404 ProblemDetails` when the title does not exist. |
| `GET /api/golden-globes` | [GoldenGlobeCatalogQuery](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeContracts.cs) | `200 PageResponse<GoldenGlobeFilmResponse>` with optional grouped-film bounds | `400 ValidationProblemDetails` for invalid query values or a reversed ceremony-year range. |
| `GET /api/golden-globes/categories` | None | `200 string[]` of distinct award categories in sorted order | None declared. |
| `PUT /api/golden-globes/imdb-link` | [GoldenGlobeImdbLinkRequest](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeContracts.cs) | `200 GoldenGlobeImdbLinkResponse`; setting an ID returns a durable refresh job, clearing returns no job | `400 ValidationProblemDetails` for an invalid group key or IMDb ID; `404 ProblemDetails` when that exact group does not exist. IDs are trimmed, lowercased, and validated before persistence. |
| `GET /api/movie-awards` | `imdbIds` (comma-separated, 1..100 valid IMDb IDs) | `200 MovieAwardRecognitionResponse[]` containing Oscar and Golden Globes records for those IDs | `400 ValidationProblemDetails` for missing, malformed, oversized, or more than 100 IDs. |

## Queries And Behavior

All paged routes use the common limits and defaults in [API contracts](API_CONTRACTS.md#paging). Invalid annotated values return 400. Pages beyond the end are empty, not clamped.

| Query DTO | Additional fields and behavior |
| --- | --- |
| `CatalogQuery` | `Search` (max 200 characters) is trimmed and matched with PostgreSQL `ILIKE` against title and normalized title. `MediaType` is `movie`, `series`, `documentary`, or `short`; `SourceType` is `movie` or `series`; `FeedTypes` is a comma-separated list of `movie`, `series_complete`, and `series_ongoing`, matching titles with at least one occurrence from a listed feed; `ContentKind` is `standard`, `documentary`, or `short`. `YearFrom` and `YearTo` each range from 1800 through 2200 and are inclusive; a reversed range is invalid. `ImdbRatingFrom` and `ImdbRatingTo` are inclusive decimals from 0 through 10; a reversed range is invalid. `Genre` and `Country` are trimmed exact array-membership filters (max 100 characters each). `SourceId` must be at least 1 and matches titles having an occurrence from that source. The catalog list and `TotalCount` always exclude titles without any occurrence; Oscar-only films are listed by the Oscar API. Supplied filters are combined. |
| `OscarCatalogQuery` | `Search` (max 200 characters) is trimmed and matched with `ILIKE` against the Oscar CSV title, normalized title, and linked title metadata. `YearFrom` and `YearTo` range from 1800 through 2200, are inclusive, and filter Oscar film year; a reversed range is invalid. `ImdbRatingFrom` and `ImdbRatingTo` are inclusive decimals from 0 through 10; a reversed range is invalid. Legacy `Category` (max 100 characters) matches one canonical category case-insensitively. Repeated `Categories` values (up to 100 values, each max 100 characters) match any canonical category case-insensitively. `CategoryFilter=true` marks an explicit selection, so an empty `Categories` set returns no films; if omitted, all categories are included. `Result` is `winner` or `nominee`; it qualifies films with a matching nomination in the selected category set. The response retains all nominations for each qualifying film. `EnrichmentStatus` is `pending`, `enriched`, `not_found`, or `temporary_error`. Supplied filters are combined. |
| `GoldenGlobeCatalogQuery` | `Search` (max 200 characters) is trimmed and matched with `ILIKE` against nomination title; `YearFrom` and `YearTo` range from 1800 through 2200 and filter ceremony year inclusively; a reversed range is invalid. `ImdbRatingFrom` and `ImdbRatingTo` are inclusive decimals from 0 through 10; a reversed range is invalid. Legacy `Award` (max 100 characters) is trimmed and matched with `ILIKE`. Repeated `Categories` values (up to 100 values, each max 100 characters) match exact award names case-insensitively. `CategoryFilter=true` marks an explicit selection, so an empty `Categories` set returns no films; if omitted, all categories are included. With the multiselect filter, category and `Result` (`winner` or `nominee`) jointly qualify a film group, while the response retains all its nominations. Without the multiselect filter, legacy `Award` and `Result` continue to filter nomination rows before grouping. `EnrichmentStatus` is `pending`, `enriched`, `problem`, `not_found`, or `temporary_error` and filters grouped titles. Supplied filters are combined; pagination applies to grouped films. |
| `MovieAwardsQuery` | Required `ImdbIds` is a comma-separated list of 1 through 100 IMDb IDs; the encoded parameter is limited to 1299 characters. IDs must be `tt` followed by 7 through 10 digits. Duplicate IDs are ignored. Results include only records matching the supplied IDs; there is no title-only association or provider lookup. |
| `OccurrencesQuery` | Only the common `Page` and `PageSize` fields. |

Catalog titles with occurrences sort by `LastSeenAt` descending, then `Id` descending. Oscar films sort by film year descending, normalized title ascending, then `Id` ascending; occurrences use the catalog ordering. Oscar-only titles can still be opened through `GET /api/titles/{id}` when following an Oscar catalog link. `PageResponse<T>` and its limits are defined in [API contracts](API_CONTRACTS.md).

The catalog routes return optional `yearBounds` and `imdbRatingBounds`, computed independently of search, category, year, rating, enrichment-status, and pagination filters. `/api/catalog` uses occurrence-backed title values, `/api/oscars` uses Oscar film values, and `/api/golden-globes` uses grouped nominee values with matching unexpired metadata-cache entries. Records without a year or IMDb rating do not contribute; each property is omitted when no usable values exist. Other paginated responses omit this metadata.

Golden Globes enrichment initially performs a type-aware exact lookup, then its Search fallback omits the media-type filter so television movies and similarly classified nominees remain discoverable. Candidate details must confirm the IMDb ID, title, actual media type, applicable year, and Golden Globe evidence before linking. A uniquely confirmed result updates the whole nomination group to the returned `movie` or `series` type; a show's premiere year is not compared with its ceremony year, while movies must be from the ceremony year or preceding year. Re-import preserves a valid automatically enriched type correction, but type corrections still clear manual links for revalidation. Safe provider cause codes are rendered as readable explanations in the UI.

Manual ID refresh uses OMDb `i=` and requires the returned IMDb ID to match exactly. A valid returned `movie` or `series` type does not block a manual link when it differs from the nomination type; the group keeps its original type, and exact-ID metadata projection allows this difference only for manual pins. Automatic matching remains type-strict, and the automatic movie-year rule does not apply to manual refresh. Manual links are pinned against batch matching and version-checked before job results are applied. Provider failures retain the selected ID; clearing a link invalidates in-flight versions and returns the group to automatic matching.

## Request DTO

| DTO | Fields and validation |
| --- | --- |
| `GoldenGlobeImdbLinkRequest` | Required `FilmId` (max 1200; canonical `ceremonyYear:movie|series:title`) and optional `ImdbId`; nonblank IDs are trimmed, lowercased, and must match `tt` plus 7 through 10 digits. Null/blank clears the pin. |

## Response DTOs

Nullable response fields are marked `?`; collection fields are lists/arrays.

| DTO | Fields |
| --- | --- |
| `YearBounds` | `MinYear`, `MaxYear` |
| `ImdbRatingBounds` | `MinRating`, `MaxRating` |
| `CatalogTitleResponse` | `Id`, `Title`, `Year?`, `MediaType`, `SourceType?`, `ContentKind?`, `ImdbId?`, `ImdbRating?`, `PosterUrl?`, `Genres`, `Countries`, `LastSeenAt?`, `OccurrenceCount` |
| `OscarFilmResponse` | `Id`, `TitleId`, `Title` (CSV title), `MetadataTitle`, `MetadataYear?`, `FilmYear`, `ImdbId?`, `EnrichmentStatus`, `EnrichmentAttemptCount`, `LastEnrichmentAttemptAt?`, `NextEnrichmentAttemptAt?`, `LastEnrichmentError?`, `MediaType`, `ImdbRating?`, `ImdbVotes?`, `Metascore?`, `Genres`, `Countries`, `Director?`, `Plot?`, `PosterUrl?`, `Runtime?`, `Awards?`, `BoxOffice?`, `Nominations` |
| `OscarNominationResponse` | `Id`, `Ceremony`, `Class`, `CanonicalCategory`, `Category`, `Name`, `Nominees`, `NomineeIds`, `Detail`, `IsWinner` |
| `GoldenGlobeFilmResponse` | `FilmId` (`ceremonyYear:nomineeType:title`), `Title`, `Year`, `NomineeType` (`movie` or `series`), `ImdbId?`, `IsImdbIdManual`, `ImdbRating?`, `PosterUrl?` (from unexpired compatible OMDb metadata cache), `EnrichmentStatus?`, `EnrichmentError?` (safe cause code, shown as a readable reason in the UI), `Nominations` |
| `GoldenGlobeImdbLinkResponse` | Normalized `ImdbId?`, `RefreshJob?` (`id`, `status`, `statusUrl`); clearing returns both values as null. |
| `GoldenGlobeNominationResponse` | `Id`, `Year`, `Award`, `IsWinner` |
| `MovieAwardRecognitionResponse` | `Id`, `ImdbId`, `Source` (`oscars` or `golden_globes`), `FilmYear?`, `CeremonyYear?`, `Ceremony?` (Oscar ceremony number), `Award`, `Name?`, `Nominees?`, `Detail?`, `IsWinner` |
| `TitleDetailsResponse` | `Id`, `Title`, `Year?`, `MediaType`, `SourceType?`, `ContentKind?`, `BroadcastRangeStartYear?`, `BroadcastRangeEndYear?`, `BroadcastRangeRaw?`, `ImdbId?`, `ImdbRating?`, `ImdbVotes?`, `Metascore?`, `Genres`, `Countries`, `Director?`, `Plot?`, `PosterUrl?`, `Runtime?`, `Awards?`, `BoxOffice?`, `FirstSeenAt?`, `LastSeenAt?`, `UpdatedAt`, `OccurrenceCount` |
| `OccurrenceResponse` | `Id`, `TitleId`, `SourceId`, `SourceName`, `SourceItemKey`, `FeedEntryId?`, `TorrentUrl`, `RawTitle`, `SourceFeedName`, `FeedType?`, `SourcePublishedAt?`, `ObservedAt?`, `Quality?`, `RipType?`, `FirstSeenAt`, `LastSeenAt` |

## Implementation And Tests

Endpoint and DTO definitions: [CatalogEndpoints.cs](../../server/src/MediaDock.Api/Catalog/CatalogEndpoints.cs), [OscarEndpoints.cs](../../server/src/MediaDock.Api/OscarAwards/OscarEndpoints.cs), [GoldenGlobeEndpoints.cs](../../server/src/MediaDock.Api/GoldenGlobes/GoldenGlobeEndpoints.cs), and [MovieAwardsEndpoints.cs](../../server/src/MediaDock.Api/Awards/MovieAwardsEndpoints.cs). Relevant API coverage: [CatalogApiTests.cs](../../server/tests/MediaDock.IntegrationTests/CatalogApiTests.cs), [OscarApiTests.cs](../../server/tests/MediaDock.IntegrationTests/OscarApiTests.cs), [GoldenGlobeApiTests.cs](../../server/tests/MediaDock.IntegrationTests/GoldenGlobeApiTests.cs), and [MovieAwardsApiTests.cs](../../server/tests/MediaDock.IntegrationTests/MovieAwardsApiTests.cs).