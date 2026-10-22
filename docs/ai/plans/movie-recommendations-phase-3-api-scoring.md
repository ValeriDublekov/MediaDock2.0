# Phase 3: Recommendation API and Scoring

Parent plan: [Personal Movie Recommendations](movie-recommendations.md).

## Goal

Return a deterministic, paged recommendation list whose personal taste inputs are isolated to the active user.

## Prerequisites

- Complete [Phase 1](movie-recommendations-phase-1-persistence.md) and [Phase 2](movie-recommendations-phase-2-vote-normalization.md).
- Use the approved Bayesian prior, missing-rating eligibility, and coefficient default/state decisions in the master plan.

## Tasks

1. Build the taste profile only from the current user's ratings. `User_Global_Avg` uses all of that user's `PersonalRating.Rating` values. For each source-qualified category, count distinct IMDb movies the user rated that are linked to a winner; duplicate nomination rows count once. Require canonical `MediaType = movie`, and for Golden Globes also require `NomineeType = movie`.
2. Use multiplier `1.0` for fewer than three distinct rated winners. Otherwise calculate `Category_Avg / User_Global_Avg` and clamp to `[0.5, 1.5]`. Do not add optional 3-10 sample shrinkage without an approved formula. An empty rating set gives every category multiplier `1.0`.
3. For candidate award events, collapse duplicate rows for the same movie/source/category/ceremony-or-year into one event; a duplicate group is a win if any row is a winner. A winner receives that category's `win_points`; a non-winning nomination receives `nomination_points`. Multiply the applicable points by the winner-derived category multiplier, sum distinct award events, and cap `Award_Component` at `10.0`. Missing configuration and `award_other` contribute zero unless explicitly configured. Do not award both win and nomination points for the same event.
4. Use `Popularity_Component = min(NormalizedVoteWeight, 10.0)`. Missing votes have stored weight `0.0`; missing-year candidates with votes use the global-median weight from Phase 2.
5. Calculate `Rating_Component = (v * ImdbRating + 1,000 * C) / (v + 1,000)`, where `C` is the average non-null IMDb rating across enriched canonical IMDb movie rows and `v` is raw `ImdbVotes`. Use `C` when votes are missing. Exclude a candidate with a missing IMDb rating. Use canonical IMDb rating data, not the current user's rating, then calculate `Final_Score = Rating_Component * W_rating + Popularity_Component * W_votes + Award_Component * W_awards`.
6. Accept required `ratingWeight`, `votesWeight`, and `awardsWeight` query parameters with each recommendation request; validate each in `[0,1]` and require their sum to equal `1.0`. Use first-use values `0.50`, `0.25`, and `0.25` in the client only; do not persist coefficients per user or store them in global `AppSetting`. Do not accept owner IDs.
7. Load candidates only from existing enriched `imdb_titles` movie rows with a non-empty title, a non-null IMDb rating, and at least one Oscar or Golden Globes nomination/win linked by IMDb ID. Torrent-only, unmatched, non-movie, pending, and incomplete-title rows are excluded.
8. Exclude movies rated by the current user and that user's `ToWatch` rows. Do not exclude a `ToDownload`-only row. Resolve the current user from validated identity and require an active linked user even when general access control is disabled; never expose another user's ratings/profile.
9. Add user-scoped `GET /api/recommendations` with the shared `PageResponse<T>` contract: default page `1`, page size `25`, maximum `100`. Sort by final score descending with IMDb ID as the deterministic tie-breaker. Return title data, each normalized component, applied coefficients, total score, and contributing award events/categories.

## Checks

- Unit tests cover empty ratings, distinct winner counts, the three-title threshold, multiplier clamps, duplicate event handling, nomination versus win points, category separation, award cap, component caps, coefficient validation, missing configuration, and final-score bounds.
- Integration tests cover candidate eligibility including missing IMDb ratings, active-user isolation in both access modes, denial without an active linked user, rated/`ToWatch` exclusions, `ToDownload` inclusion, winner-derived taste profiles, nomination scoring, type-mismatched links, unknown categories, unmatched titles, paging, and deterministic ordering.
- Run the focused `server-unit` and `server-integration` suites separately through `python -B scripts/run_tests.py "<suite>" --filter "<matching test name>"`, replacing both placeholders. Check Docker availability and do not skip relevant integration tests.

## Exit Criteria

- Results are reproducible for identical data and settings, correctly paged, and owner-scoped in enforced and open modes; coefficients use the approved defaults only in client state and are never server-persisted.
- Only approved movie-to-movie IMDb links and structured nomination outcomes affect award points; winner-only history drives taste multipliers. Excluded titles and foreign personal data never appear.
- The response exposes an understandable score breakdown and its documented final formula.