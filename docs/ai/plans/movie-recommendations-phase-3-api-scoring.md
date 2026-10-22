# Phase 3: Recommendation API and Scoring

Parent plan: [Personal Movie Recommendations](movie-recommendations.md).

## Goal

Return a deterministic, paged recommendation list whose personal taste inputs are isolated to the active user.

## Prerequisites

- Complete [Phase 1](movie-recommendations-phase-1-persistence.md) and [Phase 2](movie-recommendations-phase-2-vote-normalization.md).
- Set the remaining Bayesian prior and slider default/persistence inputs from the master plan before implementing the endpoint.

## Tasks

1. Build the taste profile only from the current user's ratings. `User_Global_Avg` uses all of that user's `PersonalRating.Rating` values. For each source-qualified category, count distinct IMDb movies the user rated that are linked to a winner; duplicate nomination rows count once. Require canonical `MediaType = movie`, and for Golden Globes also require `NomineeType = movie`.
2. Use multiplier `1.0` for fewer than three distinct rated winners. Otherwise calculate `Category_Avg / User_Global_Avg` and clamp to `[0.5, 1.5]`. Do not add optional 3-10 sample shrinkage without an approved formula. An empty rating set gives every category multiplier `1.0`.
3. For candidate award events, a winner receives that category's `win_points`; a non-winning nomination receives `nomination_points`. Multiply the applicable points by the winner-derived category multiplier, sum distinct award events, and cap `Award_Component` at `10.0`. Missing configuration and `award_other` contribute zero unless explicitly configured. Do not award both win and nomination points for the same event.
4. Use `Popularity_Component = min(NormalizedVoteWeight, 10.0)`. Missing votes have stored weight `0.0`; missing-year candidates with votes use the global-median weight from Phase 2.
5. Use `Rating_Component` as the Bayesian rating on `0..10`, using canonical IMDb rating data, not the current user's rating. The prior formula and missing-rating policy are prerequisites; do not invent them. Then calculate `Final_Score = Rating_Component * W_rating + Popularity_Component * W_votes + Award_Component * W_awards`.
6. Accept the three UI coefficients with the recommendation request; validate each in `[0,1]` and require their sum to equal `1.0`. Do not accept owner IDs or store coefficients in global `AppSetting`. Per-user persistence and first-use defaults are specified separately before implementation.
7. Load candidates only from existing enriched `imdb_titles` movie rows with a non-empty title and at least one Oscar or Golden Globes nomination/win linked by IMDb ID. Torrent-only, unmatched, non-movie, pending, and incomplete-title rows are excluded.
8. Exclude movies rated by the current user and that user's `ToWatch` rows. Do not exclude a `ToDownload`-only row. Resolve the current user from validated identity and require an active linked user even when general access control is disabled; never expose another user's ratings/profile.
9. Add user-scoped `GET /api/recommendations` with the shared `PageResponse<T>` contract: default page `1`, page size `25`, maximum `100`. Sort by final score descending with IMDb ID as the deterministic tie-breaker. Return title data, each normalized component, applied coefficients, total score, and contributing award events/categories.

## Checks

- Unit tests cover empty ratings, distinct winner counts, the three-title threshold, multiplier clamps, duplicate event handling, nomination versus win points, category separation, award cap, component caps, coefficient validation, missing configuration, and final-score bounds.
- Integration tests cover candidate eligibility, active-user isolation in both access modes, denial without an active linked user, rated/`ToWatch` exclusions, `ToDownload` inclusion, winner-derived taste profiles, nomination scoring, type-mismatched links, unknown categories, unmatched titles, paging, and deterministic ordering.
- Run the focused `server-unit` and `server-integration` suites separately through `python -B scripts/run_tests.py "<suite>" --filter "<matching test name>"`, replacing both placeholders. Check Docker availability and do not skip relevant integration tests.

## Exit Criteria

- Results are reproducible for identical data and settings, correctly paged, and owner-scoped in enforced and open modes.
- Only approved movie-to-movie IMDb links and structured nomination outcomes affect award points; winner-only history drives taste multipliers. Excluded titles and foreign personal data never appear.
- The response exposes an understandable score breakdown and its documented final formula.