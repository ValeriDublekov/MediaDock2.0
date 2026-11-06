# Movie Recommendation Performance Diagnostic (2026-11-06)

This dated diagnostic records local measurements against a restored snapshot. It is historical evidence, not a production performance estimate or part of the recommendation contract.

The isolated restored database `mediadock_prod_restore_20261029` contained 4,770 movie IMDb rows (4,180 enriched and 590 pending). The measured request found 596 eligible candidates. A warmed run of 25 sequential `GET /api/recommendations` requests (page size 25, default weights, persistent local HTTP client) returned HTTP 200 for every sample:

| Stage | p50 | p95 |
| --- | ---: | ---: |
| Candidate query and materialization | 10 ms | 15 ms |
| Candidate year/genre filtering | <1 ms | <1 ms |
| Ratings and taste-input queries | 8 ms | 9 ms |
| Taste profile | <1 ms | <1 ms |
| Recommendation settings and vote-input queries | 6 ms | 26 ms |
| Vote normalization | 1 ms | 2 ms |
| Award-event queries | 9 ms | 14 ms |
| Award-event grouping | <1 ms | <1 ms |
| Award-weight query | 1 ms | 1 ms |
| Scoring and sort | 3 ms | 5 ms |
| API request total | 46 ms | 69 ms |
| HTTP request total | 52.1 ms | 75.1 ms |

The candidate eligibility query was the dominant database stage; in-memory vote normalization and scoring were small. `EXPLAIN (ANALYZE, BUFFERS)` returned 596 candidates after scanning 4,770 movie rows, with 3,935 rows filtered out by the existing eligibility plan. With PostgreSQL JIT enabled, the plan took 45.0 ms including 30.9 ms of JIT generation; the same plan with JIT disabled took 6.5 ms. The recommendation transaction now uses `SET LOCAL jit = off`, so the setting ends with that transaction and does not change PostgreSQL configuration. Afterward, the candidate stage measured p50/p95 of 10/15 ms, versus 36/43 ms before the change.

The vote-input plan scanned 4,770 rows in 0.7 ms; the owner ratings read scanned 781 rows in 0.06 ms. Oscar and Golden Globes event-read plans took 0.45 ms and 1.48 ms respectively. No new index was added: the observed candidate plan did not identify a selective new predicate or join that justified one. These are local Windows/container timings against a restored snapshot, not production latency estimates; production hardware and load require separate measurement. No production database setting, extension, index, migration, or deployment was changed.