# Phase 1: Evidence and Shared Shell

[Master plan and phase index](ui-design-improvements-plan.md)

## Entry and Tasks

Obtain approval for the selected slice before implementation. Current status belongs in the [progress tracker](ui-design-improvements-plan-progress.md).

1. Reproduce the reported layout with synthetic local content at 1458 x 816. Confirm the notice's positioning and source owner; distinguish shell offsets from screen-specific top spacing.
2. Read only the controlling shell, navigation, notice, and selected screen context code plus nearby tests. Add verified paths and direct consumers to this file; do not infer a shared component from appearance.
3. Agree the notice severity/content rules and the compact page/result hierarchy. Preserve critical information and existing navigation behavior.
4. Implement UI-01 as a small shell slice, then any separately owned heading/context slices; validate each before continuing to another phase.

## UI-01: Unobstructed Shell and Accurate Page Context

**Priority / evidence:** P1. [Catalog](ui-design-improvements.md#catalog), [Movie Recommendations](ui-design-improvements.md#movie-recommendations), and [Oscar](ui-design-improvements.md#oscar-catalog) report poor first-viewport result visibility (High/Medium). [Golden Globes](ui-design-improvements.md#golden-globes-catalog), [Dashboard](ui-design-improvements-administration.md#dashboard), [Torrent Settings](ui-design-improvements-administration.md#torrent-settings), [Recommendation Settings](ui-design-improvements-administration.md#recommendation-settings), [OMDb](ui-design-improvements-administration.md#omdb-provider), and [Parse Log](ui-design-improvements-administration.md#parse-log) report alert/content overlap. System reports nested scrolling; repeated headings and navigation wrapping are Medium/Low candidates on other screens.

**Observation versus hypothesis:** The notice visibly covers scrolled content on several screens. Excess vertical space and repeated headings are separately recorded. A shared shell/offset defect is a plausible explanation for overlap, not a verified source owner or an explanation for every density issue.

**User impact:** Covered text and controls impede browsing and diagnosis. Competing scroll regions and stale catalog headings make location and next actions harder to understand.

**Affected areas:** Application notice, navigation and administration sidebar; page heading ownership; Catalog History > Parse log context; long catalog/admin scroll surfaces. Card/table density remains UI-06; each screen's filters stay locally owned.

## Verified Local Ownership

- [App.tsx](../../../web/src/App.tsx) owns the authenticated application shell, the authorization warning condition and text, shared page headings outside `/catalog`, and route rendering. The warning remains conditional on `authorizationEnforced !== true` and appears before authenticated route content.
- [client.css](../../../web/src/client.css), imported by [main.tsx](../../../web/src/main.tsx), owns the shell layout, sticky warning positioning, sidebar's viewport-height scroll region, and responsive navigation rules.
- [CatalogView.tsx](../../../web/src/features/catalog/CatalogView.tsx) owns the local Titles/History toggle and its page context; [HistoryView.tsx](../../../web/src/features/history/HistoryView.tsx) owns parse/scan tabs and query scope. [CatalogView.test.tsx](../../../web/src/features/catalog/CatalogView.test.tsx) verifies that switching views preserves the parse-log and scan-run requests.
- [RecommendationsView.tsx](../../../web/src/features/recommendations/RecommendationsView.tsx) owns the recommendation section heading beneath the page-level heading from [App.tsx](../../../web/src/App.tsx). Its nearby coverage is in [RecommendationViews.test.tsx](../../../web/src/features/recommendations/RecommendationViews.test.tsx).
- [App.test.tsx](../../../web/src/App.test.tsx) covers authenticated navigation and route-shell behavior, including the warning and named sidebar landmark.
- Direct consumers are the authenticated routes rendered inside `.main-shell`; no API contract, data fixture, or backend behavior is involved. Keep the warning content and condition unchanged, and use the existing synthetic session fixtures for shell tests.

**Verified decisions:** The warning is in normal document flow so it cannot remain over scrolled content; its text, severity, and conditional visibility are unchanged. The desktop sidebar retains its independent scroll region, now named "Site navigation"; the existing narrow layout returns it to normal flow. The catalog heading follows the locally selected Titles/History view; the recommendations section heading describes its distinct ranking/results content.

**Confidence / open questions:** High confidence in recorded overlap at the reviewed viewport; lower confidence in mobile behavior, the cause, and whether sidebar scrolling is necessary. It is not yet known whether the two Parse log entry points have identical scope.

**Measurable acceptance:**

- At every common validation viewport, the security notice remains readable and never covers visible content or keyboard focus while scrolling, navigating anchors, expanding details, or opening forms.
- At 1458 x 816, default Catalog and recommendation screens expose a first-row title and its primary signals without scrolling; Oscar shows title plus award context. If this requires changes beyond shell spacing, complete the target with UI-06/UI-09/UI-10 rather than hiding critical controls or notices.
- Each screen has a clear primary heading; section headings describe distinct groups. Selecting Catalog History > Parse log produces accurate history context without changing history scope.
- Every navigation destination remains reachable by keyboard at narrow widths and 200% zoom, with a visible active state and no clipped label/action. Any retained independent scroll region has a justified purpose, accessible label, and no keyboard trap.
- System-specific security guidance remains adjacent to relevant operations; shared wording is not duplicated as competing banners.

## Validation and Exit

- Run the [common validation gate](ui-design-improvements-plan-validation.md) with a long sidebar, long headings, the notice present, expanded content, and synthetic long result lists.
- Verify local shell/heading tests and focused affected screen tests using the repository runner; capture no authenticated screenshots or personal content.
- Exit when owner paths and unchanged boundaries are documented, observed overlap is resolved across the named representatives, and remaining screen-specific density work is explicitly tracked under later initiatives. This phase does not authorize consolidating routes.