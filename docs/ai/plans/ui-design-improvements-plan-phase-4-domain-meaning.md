# Phase 4: Recommendations and Awards

[Master plan and phase index](ui-design-improvements-plan.md)

## Entry and Tasks

Current status belongs in the [progress tracker](ui-design-improvements-plan-progress.md). Complete relevant Phase 3 hierarchy work before display changes; data feasibility can be checked earlier. Read only the controlling recommendation/award view, contract, and nearby fixture/test for the selected slice, then add verified paths and direct consumers here.

1. Establish which score/category fields are already available and whether category discovery is present in collapsed filters. Record unavailable data as a decision requiring approval, not an implied backend task.
2. Deliver UI-09 separately for common presentation and each verified movie/series difference; retain global-settings and personal-controls ownership boundaries.
3. Deliver UI-10 for Oscar then Golden Globes, validating source-specific category semantics rather than copying labels blindly.

## UI-09: Understand Recommendation Rank and Controls

**Priority / observation / sources:** P2. [Movie Recommendations](ui-design-improvements.md#movie-recommendations) records low score placement, weak per-title explanation, and first-viewport density (High), plus weight reasoning/order context (Medium). [Series Recommendations](ui-design-improvements.md#series-recommendations) shares these and repeats absent directors/single-year lifecycle context (Medium), while new series filters are Low. [Recommendation Settings](ui-design-improvements-administration.md#recommendation-settings) distinguishes global calculation configuration from personal weights.

**Impact / affected areas:** Help users distinguish public rating from recommendation score and explain why titles rank. Common presentation can be shared if verified; movie scoring, series data, personal coefficients, and global award/normalization settings are not presumed to have one owner.

**Dependencies:** Phase 3 card/result hierarchy; Phase 2 global save/recalculation semantics; verified score scale, contributions, normalization, tie/order behavior, and supported weight constraints. Check whether per-title contributions are supplied, safely derivable from public contracts, or unavailable; a new API response needs separate scope approval. Do not change the scoring formula for visual convenience.

**Confidence / questions:** High confidence in observed score placement and explanation gap, not that rank is incorrect. Actual contribution availability, total-weight enforcement, update latency, and reliable series lifecycle/season fields remain unverified.

**Acceptance:**

- At 1458 x 816, a first-row title exposes its explicitly labeled recommendation score alongside, but distinct from, IMDb rating. The same distinction remains readable on mobile and in Table mode where supported.
- General scoring explanation is discoverable from the controls. For each fixture title, the explanation names only verified inputs; if numerical contributions exist, the displayed breakdown agrees with the existing calculation within documented rounding. Otherwise state the available signals without fabricating a precise breakdown.
- Each weight has an associated name and current percentage; total and validity state are adjacent and announced. Keyboard adjustment and focus work without unexpected redistribution. Preserve verified constraints and communicate whether edits immediately rerank, require an action, or persist.
- Result ordering is explicit and unchanged from the verified ranking contract. Loading/failure after weight/filter changes cannot display stale results as newly calculated or trigger global recalculation implicitly.
- Movie and series wording remains correct. Hide unavailable director noise; show a series year span or ended/ongoing state only when reliably known, with an explicit unknown treatment otherwise. No season/episode ingestion or new ongoing/season filter is implied.
- Personal weight adjustments are clearly separate from global defaults/award overrides and movie recalculation. Series request-time behavior is explained only after contract verification.

## UI-10: Category-Level Awards with Source Separation

**Priority / observation / sources:** P2. [Oscar Catalog](ui-design-improvements.md#oscar-catalog) lacks visible category outcomes and needs distinct Oscar facts versus other awards (High); [Golden Globes](ui-design-improvements.md#golden-globes-catalog) promises categories but displays aggregate totals (High). Discoverability/density are Medium. More filters was collapsed and uninspected, so absence of category filtering is not established.

**Impact / affected areas:** Let users determine what a title was nominated for or won, without confusing totals, Oscar/Golden Globes records, or provider-supplied general awards. Affect award cards/details and available category discovery; recommendation award summaries may reuse presentation only if their contracts support it.

**Dependencies:** Verify category records, source, ceremony year versus release/series year, win/nomination flags, aggregation and pagination; inspect existing disclosure/filter behavior first. Use Phase 3 card hierarchy and result controls. Do not equate OMDb award prose with structured source category facts.

**Confidence / questions:** High confidence that visible cards omit category outcomes, not that the data/detail path is absent. Completeness, existing category filters, multi-ceremony representation, and whether awards count wins as nominations must be resolved from contracts/fixtures.

**Acceptance:**

- From a title's award total, category details are visible or reachable by one clearly labeled, keyboard-operable disclosure/navigation action. Each available entry identifies source, category, ceremony context, and win/nomination outcome without color-only meaning.
- Oscar category facts, Golden Globes facts, and general provider awards have distinct labels/grouping; totals are traceable to verified source aggregation and never double-counted merely to populate the UI.
- Category discovery already available under More filters is made findable if confirmed. New filters require verified query support and separate approval; absent data is shown honestly rather than populated from provider prose.
- Default first-viewport award context is readable alongside title/rating; opening categories does not truncate controls, change counts, or cause sticky overlays. Long categories and multiple ceremonies remain usable at narrow widths.
- Fixtures cover winner, nominee-only, multiple categories/ceremonies, mixed movie/series Golden Globes entries, and missing category links. Clearly distinguish zero known awards from unavailable category details.

## Validation and Exit

Apply the [common validation gate](ui-design-improvements-plan-validation.md) with long category names, missing fields, varied weights, equal scores, and multiple ceremonies. Use focused web checks; if a separately approved API slice is necessary, map all direct readers and run affected backend unit/integration checks through the repository runner, with Docker availability established for integration.

Exit when a user can identify recommendation versus IMDb rating, understand supported ranking signals, and reach source-specific category outcomes accurately in the approved fixture scenarios. Preserve existing formulas, defaults, category facts, and movie/series semantics; document unavailable data as an unresolved dependency, not a completed feature.