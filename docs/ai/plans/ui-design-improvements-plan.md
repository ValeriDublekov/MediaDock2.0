# UI Design Improvement Plan

## Status and Goal

Draft for review, 2026-10-29. This is a proposed implementation sequence, not approval to change the application, deployed data, settings, or navigation. This file is the master index; each independently verifiable implementation step has a separate phase file below. No implementation phase has started.

Improve safe operation, browsing efficiency, and interpretation of titles and scores without weakening security notices or changing data semantics. Preserve the existing visual language and useful controls rather than redesigning the product wholesale.

## Evidence and Limits

- Sources: the [catalog and personal-screen backlog](ui-design-improvements.md) and [administration backlog](ui-design-improvements-administration.md), both reviewed on 2026-10-29, primarily at 1458 x 816 desktop.
- Recorded observations are evidence; proposed remedies in those backlogs remain candidates. High/Medium/Low below retain the reviewers' labels, not estimates of engineering severity or effort.
- Inspection was predominantly visual/read-only. Selected responsive layouts, keyboard flows, expanded filters, loading/error states, and mutation outcomes were not exercised. A visible control does not establish its implementation, contract, or accessibility correctness.
- No application source was inspected for this synthesis. Screen names indicate functional scope, not verified component ownership. The [architecture guide](../ARCHITECTURE.md) describes the web/API boundary and separate job lifecycle; repository guidance does not establish what is deployed.
- No new deployed-site interaction, screenshots, account data, credentials, or raw operational logs are required or retained by this plan.

## Main Opportunities

1. Keep security information visible without covering content, and recover useful first-viewport space.
2. Make consequential actions explicit, validatable, and stateful before optimizing their appearance.
3. Reduce scanning cost through consistent card hierarchy, genuinely compact tables, and accessible result context.
4. Explain domain meaning: personal versus public ratings, recommendation scores, mixed movie/series scope, and category-level award outcomes.

## Priorities and Phase Index

P1 addresses operational/data risk or a broad usability blocker. P2 addresses repeated browsing and comprehension problems after the relevant foundations. These scheduling bands do not downgrade a source observation labeled High. Within each phase, the initiative order is the proposed priority order; there are no precise effort estimates.

| Step | Initiatives and source evidence | Reason for order | Dependency / exit |
| --- | --- | --- | --- |
| [Phase 1: Evidence and shared shell](ui-design-improvements-plan-phase-1-shell.md) | UI-01, P1: alert overlap, navigation and page context; High first-viewport concerns, repeated Medium overlap | Broad benefit; establishes scroll, heading, and responsive constraints for subsequent work | Approval and local ownership check; shell acceptance gate |
| [Phase 2: Safe operations and diagnostics](ui-design-improvements-plan-phase-2-operations.md) | UI-02 through UI-05, P1: intent, lifecycle, input validity, diagnostic meaning; multiple High observations | Deployment, imports, account changes, provider requests, retries, saves, and recalculation carry operational/data risk | Per-workflow contract verification; safe synthetic success/failure scenarios |
| [Phase 3: Browsing and personal libraries](ui-design-improvements-plan-phase-3-browsing.md) | UI-06 through UI-08, P2: density, finding/position, scope and rating meaning; High and Medium observations | Reuses shell and action decisions across frequent browsing tasks | Phase 1 offsets; Phase 2 action conventions where mutations appear; browse/reflow gate |
| [Phase 4: Recommendations and awards](ui-design-improvements-plan-phase-4-domain-meaning.md) | UI-09 and UI-10, P2: recommendation explanation and award categories; High observations | Domain-specific interpretation builds on readable results; data availability must be established first | Phase 3 hierarchy; verified score/category data; domain comprehension gate |

Phase 1 starts with the smallest local evidence/ownership checks, not a new whole-product review. Each later phase is separately approved and delivered as coherent, independently testable slices. Safe-action conventions are shared design work, not a mandate for one generic workflow engine. Phase 4 data-feasibility checks may run alongside Phase 2; its display changes depend on Phase 3. P1 safety work need not wait for unrelated browsing refinements.

## Coverage and Ownership Boundaries

| Reviewed screen or entry point | Initiative coverage | Proposed responsibility, to verify locally |
| --- | --- | --- |
| Catalog Posters / Table | UI-01, UI-02, UI-06, UI-07 | Shell plus catalog controls, card/table renderers, and result actions |
| Catalog History > Parse log | UI-01, UI-03, UI-05, UI-07 | Catalog heading/context plus history diagnostics; relationship to standalone route unverified |
| Movie / Series Recommendations | UI-01, UI-02, UI-06, UI-07, UI-09 | Common recommendation presentation if confirmed; separate movie/series scoring/data contracts |
| Favorites | UI-02, UI-03, UI-06, UI-07, UI-08 | Personal-list state and mixed-type wording; do not infer ownership from catalog appearance |
| Ignored Titles | UI-02, UI-03, UI-06, UI-08 | Ignored-list action semantics and accessible target naming |
| Personal Ratings | UI-01, UI-06, UI-07, UI-08 | Rating search, presentation, provenance, and date semantics |
| Import Ratings | UI-02, UI-03, UI-08 | Upload validation, merge contract, and import result presentation |
| Oscar / Golden Globes | UI-01, UI-02, UI-06, UI-07, UI-08, UI-10 | Reusable browsing where proven; separate source-specific award/category interpretation |
| Administration System | UI-01, UI-02, UI-03, UI-05 | Deployment control/status and local security guidance |
| Administration Dashboard | UI-01, UI-03, UI-05 | Statistics freshness, breakdown tables, and available diagnostic destinations |
| Torrent Settings | UI-01, UI-02, UI-03, UI-04 | Feed management, ingestion status, and matching-setting validation |
| Recommendation Settings | UI-01, UI-02, UI-03, UI-04, UI-09 | Global defaults/overrides and movie recalculation; not personal score sliders |
| OMDb Provider | UI-01, UI-02, UI-03, UI-04, UI-05 | Credential draft/persistence, test semantics, quota and history |
| Users | UI-01, UI-02, UI-03 | Account-status operations, identity context, and table reflow |
| Administration Parse Log | UI-01, UI-02, UI-03, UI-05, UI-07 | Outcome semantics, disclosure, retry lifecycle, and result position |

Similar layouts justify shared design rules, not source consolidation by default. In each approved slice, identify the decision owner, direct consumers, API fields, fixtures, and tests; cite the actual paths in the phase file before editing. Treat unrelated scoring, parsing, import merge rules, identity, authorization, and persistence as unchanged contracts unless a separate change is approved.

## Common Validation and Acceptance Gate

- Use synthetic/local fixtures, not production mutations or captured personal data. Cover default, empty, missing-metadata, loading, validation-error, service-failure, and long-content states relevant to the slice.
- Compare the reviewed 1458 x 816 viewport with 1024 x 768, 768 x 1024, 390 x 844, 320 x 800, and a wide desktop such as 1920 x 1080. These are proposed validation targets, not already reviewed evidence. Check 200% zoom and narrow reflow.
- No alert, sticky toolbar, header, or save bar may obscure text, a focused control, the first visible data row, or another sticky surface. No unintended page-level horizontal overflow; genuinely tabular data may use a labeled, keyboard-operable horizontal region.
- Validate keyboard order, visible focus, accessible names and control grouping, table/disclosure semantics, and announced asynchronous results. Do not rely on color alone. Target WCAG 2.2 AA: text contrast 4.5:1 (3:1 for large text), control/state contrast 3:1, and 24 CSS-pixel minimum targets or compliant spacing; prefer 44-pixel primary touch targets.
- Preserve existing filter/view behavior, result counts, ranking semantics, personal data ownership, security information, and available detail. A layout change must not silently change a query or mutation.
- After approved web changes, run the narrowest relevant `python -B scripts/run_tests.py web --filter <verified-file-pattern>`, plus web lint/build per the [testing guide](../TESTING.md). Use affected server unit/integration suites only when contracts/backend behavior are touched; check Docker availability for integration and report a blocker if unavailable. Include affected integration suites for substantial functional changes. Use stubbed external providers and the existing runner for every test.
- Record pass/fail against each initiative's acceptance criteria and the common gate. Stop that slice if a contract or required validation cannot be established; do not substitute deployment for testing.

## Out of Scope

- Implementation, deployment, settings edits, uploads, provider tests, saves, retries, recalculation, account changes, and other live mutations during planning or observational review.
- New scoring formulas, changed award facts, import merge policies, parser behavior, schema migrations, credential storage, authentication/authorization, or network-boundary changes. If needed, propose a separate reviewed contract change.
- Automatic consolidation/removal of the two Parse log entry points; new series filters; season/episode metadata ingestion; universal page-size/jump controls; or a fixed four-column rule without evidence.
- Removing security warnings, exposing stored keys, retaining personal review data, a wholesale redesign, speculative component rewrites, precise effort promises, or adding controls merely for visual symmetry.

## Unresolved Decisions

1. Which repeated layouts actually share components, and what differs between repository source and deployment? Resolve narrowly per slice.
2. Which current APIs expose per-title score contributions, award categories, diagnostic links, series lifecycle data, import outcome counts, and operation progress? Missing data is a scope decision, not permission to invent it.
3. Does OMDb testing consume shared quota, use saved or draft credentials, and persist request history? What does manual check/deploy know before checking? Verify before writing action promises.
4. Which unavailable/failed/terminal outcomes are retryable, and what recovery/status reads exist after a timeout? Is dashboard freshness generation time or fetch time?
5. Are personal rating provenance and vote/import/recorded dates distinguishable? Are favorites and Golden Globes intentionally mixed-type, and are both Parse log entry points equivalent?
6. What responsive and assistive-technology issues remain beyond the desktop review? Validate before declaring an observed accessibility symptom resolved.

## Planning Handoff

Only this index and its four phase files are new planning artifacts; the observation backlogs remain unchanged. Validate documentation with `python -B scripts/check_markdown_links.py`. Application tests are not part of this documentation-only task. Implementation requires explicit scope approval, local ownership/contract checks, and the relevant phase gate.