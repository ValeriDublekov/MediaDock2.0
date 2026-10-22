# Phase 1: Persistence and Migration

Parent plan: [Personal Movie Recommendations](movie-recommendations.md).

## Goal

Add the smallest schema surface needed for global vote normalization and configurable source-qualified award points without changing canonical identity or personal-data ownership.

## Prerequisites

- Apply the accepted category-key and freshness decisions in the master plan.
- The shared settings row may currently be absent. Preserve its current provider and matching defaults, and create `general_recommendation_settings.id = 1` with vote-normalization defaults so database invalidation always has a target; settings API responses must remain unchanged.

## Tasks

1. Add a singleton `GeneralRecommendationSettings` entity/table with positive `MedianPeriodYears` (default `5`), `RequiresVoteRecalculation` (default `false`), last-success timestamp, and the period used by that calculation. Add database checks for the period and flag.
2. Ensure migrations create the recommendation settings row with defaults if it is absent and move any existing vote-normalization state out of `settings` without data loss. Set `RequiresVoteRecalculation = true` when existing movie rows receive initial zero weights; leave it `false` for an empty movie catalog.
3. Add non-null `NormalizedVoteWeight` to `ImdbTitle` with a database default of `0.0`. This value is also the defined fallback for missing votes; the dirty flag distinguishes uncalculated existing data from a fresh zero.
4. Add a normalized `CategoryKey` to `GoldenGlobeAward` while retaining `Name`. Add a source-qualified award-category alias dictionary (unique by source and raw name) with migration-seeded aliases and stable internal keys. Existing award rows must be backfilled; unknown names map to `award_other`. Oscar `CanonicalCategory` remains its canonical category input.
5. Add `recommendation_award_weights` with a unique `(award_source, category_key)` key and nonnegative `win_points` and `nomination_points`. Seed mapped Oscar keys with `3.0`/`1.0` and mapped Golden Globes keys with `2.0`/`0.5`; seed `award_other` with `0.0`/`0.0`. Missing configuration also resolves to zero and must not overwrite saved administrator values.
6. Add the EF Core migration(s) with defaults, constraints, indexes, and relationships. Move vote-normalization settings from `settings` to `general_recommendation_settings` without changing source-owned IMDb identity or `personal_ratings` ownership/rows.
7. Follow migration safeguards in [data contracts](../DATA_CONTRACTS.md) and [operations](../OPERATIONS.md); inspect migration effects before applying outside disposable test databases.

## Checks

- Persistence/migration tests cover absent settings rows, unchanged provider API defaults, initial recommendation dirty-flag state, preservation of existing settings and recommendation state, the period default/check, non-null zero vote-weight default, category-key backfill/aliases, source-category uniqueness, both nonnegative outcome-point fields, and seeded point defaults.
- Verify the migration does not rewrite IMDb identity links or personal-rating ownership.
- Run the persistence/migration integration tests through `python -B scripts/run_tests.py server-integration --filter "<matching test name>"`, replacing the placeholder with the actual filter. Check Docker availability and do not skip relevant integration tests.

## Exit Criteria

- The migration preserves existing data, guarantees the singleton settings row needed by the trigger, and preserves API-visible defaults.
- Existing mapped categories receive approved defaults; unknown/unconfigured categories resolve to zero without overwriting administrator values.
- Vote weights default to `0.0`, with the dirty flag accurately identifying existing rows that still need calculation; no source identity or owner-scoping contract changes.