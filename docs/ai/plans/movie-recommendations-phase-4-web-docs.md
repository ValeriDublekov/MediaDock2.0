# Phase 4: Web Experience and Documentation

Parent plan: [Personal Movie Recommendations](movie-recommendations.md).

## Goal

Make recommendation settings and results usable in the existing React application, then document the final contracts and operational behavior.

## Prerequisites

- Complete [Phase 2](movie-recommendations-phase-2-vote-normalization.md) and [Phase 3](movie-recommendations-phase-3-api-scoring.md); consume their final DTOs and decisions rather than duplicating scoring rules in the browser.

## Tasks

1. Add API client methods and types for recommendation settings, recalculation, and paged recommendations, including validated user slider coefficients.
2. Own the web UI work: extend the configuration view for period, win/nomination points, dirty state, and recalculation; add the recommendations view and navigation with three sliders constrained to coefficients summing to `1.0`, initialized to `0.50 / 0.25 / 0.25` for rating/votes/awards and kept in client state only. Add the shared year-range control and a genre multiselect populated from IMDb metadata.
3. Show loading and error states in both views, plus empty and paged states in recommendations. Explain the final score components and award events. Display the warning “New metadata detected. Recommended scores may be inaccurate. Recalculate vote weights.” while the dirty flag is true; handle timeout and the future `202 Accepted`/polling flow.
4. Add web tests for settings reads/writes, the warning and recalculation lifecycle, coefficient constraints, year/genre recommendation filters, paging, candidate exclusions, and empty/error states.
5. Update API, data-schema, data-contract, data-behavior, and identity/access documentation. Keep the master decision section and acceptance criteria synchronized with implementation.

## Checks

- Run focused web tests with `python -B scripts/run_tests.py web --filter "<matching test name>"`, replacing the placeholder with the actual test name or unique filter fragment.
- Run `python -B scripts/check_markdown_links.py` after documentation changes.
- Use the repository test runner for every test run; see the [testing guide](../TESTING.md).

## Exit Criteria

- Settings and recommendations use the server's authoritative contracts and do not reimplement scoring in the browser.
- Year and genre filters are applied by the server before paging, and the genre selector lists distinct genres from all IMDb title rows.
- Main UI states, paging, and score explanation are covered by web tests.
- Documentation reflects the approved behavior, security boundaries, and operational freshness policy; local links validate.