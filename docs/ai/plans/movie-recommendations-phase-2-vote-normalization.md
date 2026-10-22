# Phase 2: Admin Settings API and Vote Normalization

Parent plan: [Personal Movie Recommendations](movie-recommendations.md).

## Goal

Expose administrator-owned recommendation settings and calculate global IMDb vote weights as one consistent, atomic result.

## Prerequisites

- Complete [Phase 1](movie-recommendations-phase-1-persistence.md).
- Apply the master-plan decisions for category aliases, dirty-flag invalidation, locking, and the 10-second synchronous timeout.

## Tasks

1. Read and update vote-normalization values through `GeneralRecommendationSettings`; extend admin settings contracts/API with `MedianPeriodYears`, category win/nomination points, and `RequiresVoteRecalculation`. Preserve current API defaults and never return the OMDb key or other credentials.
2. Normalize category labels during import through the seeded alias dictionary. Store stable keys on award records; map unknown raw names to `award_other` and preserve the raw name for display. Keep configured category values when imports add aliases or categories.
3. Add `POST /api/settings/recommendations/recalculate-votes`. When access control is enforced it requires an active administrator. In open mode the policy is unprotected, so retain the existing loopback/trusted-network boundary and do not imply that open mode authenticates an administrator.
4. Compute over one row per canonical IMDb movie. For known years, use `period_start = year - (year % median_period_years)` and calculate a median for each period. Include zero votes. Sort values and use the middle value, or the mean of the two middle values for an even count; use divisor `1` if the median is zero.
5. Compute a global median from all movie rows with a present `ImdbVotes` value, including zeroes and rows with no year. For a movie with a missing year but present votes, use this global median. A missing vote count yields `NormalizedVoteWeight = 0.0`. Compute weights with floating-point division: `raw_index = total_votes / applicable_median_votes`; `normalized_vote_weight = log10(raw_index + 1)`. Zero votes yield `0`; votes equal to the median yield approximately `0.301`.
6. Use a PostgreSQL trigger to set `RequiresVoteRecalculation = true` on movie insertion and changes to `Year`, `ImdbVotes`, or `MediaType`; changing `MedianPeriodYears` also marks the weights dirty. The migration guarantees the `general_recommendation_settings` singleton exists. The trigger must cover every writer, including direct SQL paths.
7. At recalculation start, acquire a table lock on `imdb_titles` that blocks concurrent inserts/updates. In one transaction, calculate and write every weight, update successful-calculation metadata, and clear the dirty flag. A writer waiting on the lock continues after commit and sets the flag back to `true`. On error or timeout, rollback and leave the dirty flag `true` and prior successful metadata intact.
8. Enforce a 10-second request/execution timeout. A timeout returns a timeout error and leaves recalculation required. If the synchronous calculation exceeds this limit at future scale, return `202 Accepted`, enqueue the existing background job, keep the state dirty while pending/running, and let the UI poll until it becomes `false`.
9. Return calculation time, period, movie counts, global-fallback count, missing-vote count, and any other defined skip counts. Expose dirty state, last success, and settings values through the admin API for the web UI.

## Checks

- Unit tests cover period boundaries, odd/even medians, period/global zero medians, missing-year global fallback, zero/missing votes, logarithmic values, and popularity capping at `10.0`.
- Integration tests cover settings-row migration/defaults, trigger invalidation for inserts and input changes, period-change invalidation, authorization, alias and unknown-category mapping, seeded win/nomination points, credential omission, 10-second timeout rollback, lock serialization, summary counts, and complete commit or rollback.
- Verify a waiting metadata write makes the flag dirty again after a successful recalculation commits.
- Run each relevant backend suite separately through `python -B scripts/run_tests.py "<suite>" --filter "<matching test name>"`, replacing both placeholders (`server-unit` or `server-integration`). Check Docker availability and do not skip relevant integration tests.

## Exit Criteria

- A successful run produces a complete, internally consistent set of global weights and an accurate last-calculated state.
- Failed runs do not publish partial weights or a new successful timestamp.
- The admin UI and API show accurate defaults and freshness without exposing credentials or changing network/access boundaries.