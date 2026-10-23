# Movie Recommendation Engine

## Ownership and settings

The engine scores canonical `imdb_titles` movie rows. Its singleton `general_recommendation_settings` row stores the global calculation settings; recommendation coefficients remain request-local. No parallel catalog or `global_settings` table is used.

| Setting | Default | Validation |
| --- | ---: | --- |
| `median_period_years` | `5` | Positive integer |
| `bayesian_default_c` | `5.5` | `0..10` |
| `bayesian_m_ratio` | `0.25` | Greater than `0` and at most `1` |
| `oscar_win_points` | `3.0` | Nonnegative |
| `oscar_nom_points` | `1.0` | Nonnegative |
| `gg_win_points` | `2.0` | Nonnegative |
| `gg_nom_points` | `0.5` | Nonnegative |

`recommendation_award_weights` retains optional source/category overrides. Rows with `is_override = false` use the source-level defaults above. A row with `is_override = true` supplies its own win and nomination points; a missing row uses the source-level defaults. Existing customized rows are preserved and marked as overrides by the migration.

## Vote periods and popularity

For a movie with year `Y`, the period starts at:

```text
Period_Start = Y - (Y % median_period_years)
```

The period median uses all movie vote counts in that period, including zeroes. An even number of values uses the mean of the middle pair. If the period has no median, or its median is below `1`, the effective median is `1000`. A title without a year also uses `1000`; missing-year titles are not grouped into a synthetic period.

For vote count `v`, the stored popularity weight is:

```text
NormalizedVoteWeight = min(10, log10(v / Period_Median_Votes + 1) * 3.33)
```

Missing or negative vote counts are treated as zero. A missing vote count therefore has popularity `0`; a title with exactly the median votes scores about `1.0` before any cap.

Weights are stored per canonical movie and recalculated together in one transaction. A PostgreSQL trigger marks them dirty when a movie is inserted or its year, vote count, or media type changes, and when `median_period_years` changes. The formula-change migration also marks an existing movie catalog dirty so old-format weights are not mistaken for current values.

## Bayesian rating

For each recommendation request, the engine obtains the candidate's period median from current movie vote data and applies the `1000` fallback when needed. The effective prior strength is:

```text
m = Period_Median_Votes * bayesian_m_ratio
```

The rating component is:

```text
Bayesian_Rating = (v * IMDb_Rating + m * bayesian_default_c) / (v + m)
```

Missing votes use `v = 0`, which yields `bayesian_default_c`. The positive ratio and median fallback keep the denominator nonzero. Candidates without an IMDb rating are excluded before scoring.

## Personal taste and award points

The user's global average uses all of that user's personal IMDb ratings. For each source-qualified award category, the category average uses distinct rated movies with a nomination or win in that category. Duplicate rows for one movie/category do not increase the count.

- Fewer than three rated movies, or a zero/missing global average, gives a multiplier of `1.0`.
- Otherwise, the multiplier is `Category_Avg / User_Global_Avg`, clamped to `[0.5, 1.5]`.

Each distinct movie/source/category/ceremony-or-year award event contributes either win points or nomination points, never both. Duplicate event rows collapse into one event; if any duplicate is a winner, the event is scored as a win. The applicable global default or explicit category override is multiplied by the taste multiplier. Adjusted event points are summed and capped at `10.0` for the award component.

## Composite score and eligibility

Each request supplies `ratingWeight`, `votesWeight`, and `awardsWeight`. Each must be in `[0,1]`, and their decimal sum must equal `1.0`. The web defaults to `0.50`, `0.25`, and `0.25`; values stay in client/request state.

```text
Final_Score = (Bayesian_Rating * ratingWeight)
            + (NormalizedVoteWeight * votesWeight)
            + (Award_Component * awardsWeight)
```

Candidates must be enriched IMDb movie rows with a nonempty title and IMDb rating, linked by IMDb ID to an Oscar or qualifying Golden Globes nomination, and not already rated by the active user. Golden Globes links require both the nominee type and canonical IMDb media type to be `movie`. The active user's `ToWatch` movies are also excluded; `ToDownload`-only movies remain eligible. Results sort by final score descending, then IMDb ID ascending. Year and genre filters are applied before paging.

The API resolves the active linked user on the server and never accepts a client-supplied owner ID. Personal ratings and taste profiles are scoped to that user. In enforced mode, recommendation settings and vote recalculation remain administrator-only.

## Freshness and verification

The settings API exposes the dirty flag and last successful vote-weight recalculation. Recalculation is synchronous with a 10-second execution limit; a timeout rolls back and leaves the dirty flag set. Recalculate weights after a period change or when the UI reports stale metadata.

Run focused checks through the repository runner:

```powershell
python -B scripts/run_tests.py server-unit --filter "FullyQualifiedName~MovieRecommendationScorerTests|FullyQualifiedName~MovieVoteNormalizerTests"
python -B scripts/run_tests.py server-integration --filter "FullyQualifiedName~MovieRecommendationsApiTests|FullyQualifiedName~RecommendationSettingsApiTests"
python -B scripts/run_tests.py web --filter RecommendationViews.test.tsx
```

Database migrations must follow the isolated/staging and deployment safeguards in [data contracts](DATA_CONTRACTS.md) and [operations](OPERATIONS.md). The server implementation and tests are the authoritative behavior; this reference describes the current calculation contract.
