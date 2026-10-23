# Personal Movie Recommendations

## Goal

Rank recommendation candidates for the active user using personal ratings, award nominations and wins, and time-normalized IMDb vote popularity. The plan covers IMDb titles already known to MediaDock, not a complete copy of the IMDb catalog.

## Planning Layout

This master plan records scope, constraints, architecture decisions, remaining implementation inputs, phase dependencies, and acceptance criteria. Each implementation phase has its own tasks, checks, and exit criteria because the phases are independently verifiable. This follows the [repository plan-file instructions](../../../.github/instructions/mediadock.instructions.md#plan-files).

| Phase | Scope | Detailed plan |
| --- | --- | --- |
| 1 | Persistence and migration | [Phase 1](movie-recommendations-phase-1-persistence.md) |
| 2 | Admin settings and vote normalization | [Phase 2](movie-recommendations-phase-2-vote-normalization.md) |
| 3 | Personal recommendation API and scoring | [Phase 3](movie-recommendations-phase-3-api-scoring.md) |
| 4 | Web experience and documentation | [Phase 4](movie-recommendations-phase-4-web-docs.md) |

Phases 1 and 2 establish the data and global vote weights. Phase 3 depends on those contracts and on approval of the scoring and candidate decisions below. Phase 4 consumes the final API and settings contracts.

## Domain Constraints

- Do not add parallel `movies` or `global_settings` tables. `imdb_titles` remains the shared IMDb identity and metadata owner; source-owned Oscar, Golden Globes, and torrent records link by IMDb ID, never by title alone.
- Personal ratings and watch state are always scoped to the active user. Never aggregate across users or accept a client-supplied `userId`.
- Keep the existing network boundary: the API defaults to loopback. LAN access is unauthenticated and must retain its specific-interface bind and host firewall allowlist. Do not broaden the bind or firewall scope for these endpoints.

See [data schema](../DATA_SCHEMA.md), [data contracts](../DATA_CONTRACTS.md), [personal-data API rules](../API_IDENTITY_AND_PERSONAL_DATA.md), [catalog and award API rules](../API_CATALOGS_AND_AWARDS.md), [shared API contracts](../API_CONTRACTS.md), and [architecture](../ARCHITECTURE.md).

## Architecture & Logic Decisions

1. **Final score and coefficients:** normalize each component to `0.0..10.0`: `Rating_Component` is the Bayesian rating, `Popularity_Component = min(NormalizedVoteWeight, 10.0)`, and `Award_Component = min(sum(Adjusted_Award_Points), 10.0)`. For the Bayesian rating, use `C = average(ImdbRating)` across enriched IMDb movie rows with a non-null rating and prior strength `m = 1,000` raw IMDb votes: `Rating_Component = (v * ImdbRating + m * C) / (v + m)`. When votes are missing, use `C`. Combine components as `Final_Score = (Rating_Component * W_rating) + (Popularity_Component * W_votes) + (Award_Component * W_awards)`. Each UI coefficient is in `[0, 1]` and the three sum to `1.0`, so the final score remains in `0..10`.
2. **Candidate eligibility:** include only enriched, titled movie IMDb records with a non-null IMDb rating, linked by IMDb ID to at least one Oscar or Golden Globes nomination, whether or not it won. Do not include torrent-only or unmatched titles. Exclude a Golden Globes record unless its nominee type and linked IMDb media type are both `movie`.
3. **Missing vote data:** for a known year, use that movie's configured year-period median. For a missing year but present vote count, use the global median across movie vote counts from all periods, including year-missing rows and zeroes. For a missing vote count, set `NormalizedVoteWeight = 0.0` and use the Bayesian prior mean for `Rating_Component`. Use `1` as divisor when the applicable median is zero.
4. **Taste multiplier:** compute category taste from distinct rated winner movies only. Use `1.0` for fewer than three; otherwise compute `Category_Avg / User_Global_Avg` and clamp it to `[0.5, 1.5]`. Shrinkage for samples of 3-10 is optional; do not add it in the first implementation without an explicitly approved formula.
5. **Outcome-specific award points:** configure win and nomination points separately for each source-qualified category. Seed mapped Oscar categories with win `3.0` and nomination `1.0`; seed mapped Golden Globes categories with win `2.0` and nomination `0.5`. A winning event uses win points, not both values; a non-winning nomination uses nomination points. Collapse duplicate rows for one movie/source/category/ceremony-or-year into one event, treating it as a win if any row is a winner. Multiply each event's base points by its category taste multiplier, sum distinct award events, and cap the result at `10.0`. Missing/unconfigured categories and `award_other` default to `0.0`; an administrator may explicitly configure `award_other`.
6. **Freshness and concurrency:** store `RequiresVoteRecalculation` (default `false`), `MedianPeriodYears`, and last-success metadata in the singleton `GeneralRecommendationSettings` table. Use a PostgreSQL trigger to set the flag `true` when a movie is inserted or its year, votes, or media type changes; changing `MedianPeriodYears` also sets it. The migration must create `general_recommendation_settings.id = 1` if absent and set the flag `true` when existing movies need their initial calculation. Recalculation locks `imdb_titles` against concurrent input writes, calculates and writes all weights in one transaction, and clears the flag only on successful commit. A waiting metadata write then sets the flag back to `true`.
7. **Category keys:** map raw award names to stable internal keys using a database-backed, migration-seeded alias dictionary. Store the resolved key on imported Golden Globes award records and retain the raw name for display. Use Oscar `CanonicalCategory` as the canonical Oscar key. Map unknown categories to `award_other`; keep source qualification in the weight key. Add aliases for renamed awards by mapping the new raw name to the existing key.
8. **Recalculation limit:** keep the endpoint synchronous with a 10-second execution/request timeout. A timeout rolls back the transaction and leaves the dirty flag `true`, so the settings warning remains visible. If future dataset growth exceeds this limit, pivot to `202 Accepted` and the existing background-job model; while a job is active, the UI polls settings until the dirty flag returns to `false`.

## Approved Phase 3 Inputs

- Use the enriched movie catalog mean and a 1,000-raw-vote prior for Bayesian ratings. Candidates without an IMDb rating are excluded; missing vote counts use the catalog mean.
- First-use coefficients are `W_rating = 0.50`, `W_votes = 0.25`, and `W_awards = 0.25`. The web view uses three sliders that keep the total at `1.0`; page changes preserve the current values, while reopening the view starts from these defaults. Keep them in client/request state only; do not persist them per user or globally. Every request must provide all three and validate each range and the sum.

The eight architecture decisions and these Phase 3 inputs are approved.

## Acceptance Criteria

- All four phase exit criteria are met. No parallel catalog or global-settings table is added, and source-owned records remain linked to the canonical IMDb title by IMDb ID.
- Vote weights remain global per canonical movie, and recalculation publishes a complete result atomically while keeping freshness state accurate after input changes, failures, and timeouts.
- Recommendations follow the approved eligibility and scoring decisions, are deterministic and paged, and remain isolated to the active user without changing the existing access or network boundaries.
- Recommendation results support inclusive year-range and any-selected-genre filters before paging; genre options come from distinct values across `imdb_titles`.
- The approved Bayesian prior, missing-rating candidate exclusion, slider defaults, and client-only coefficient state are used consistently by the API, web UI, and documentation.
- In enforced mode, global recommendation settings and recalculation remain administrator-only, while personal results require the active linked user. The web displays server-scored results and award events without duplicating candidate eligibility or scoring logic.

## Verification

For this plan-only change, run `python -B scripts/check_markdown_links.py`. For implementation, use the repository runner and the narrowest applicable suites described in the [testing guide](../TESTING.md); integration tests require Docker and must not be skipped when relevant.
