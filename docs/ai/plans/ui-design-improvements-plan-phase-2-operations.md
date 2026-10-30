# Phase 2: Safe Operations and Diagnostics

[Master plan and phase index](ui-design-improvements-plan.md)

## Entry and Implementation Order

Current status belongs in the [progress tracker](ui-design-improvements-plan-progress.md). Choose one problem, not this whole phase. Use verified shell rules only when touching sticky layout. Check the selected operation's current intent, validation, pending/result states, contract, and nearby tests locally; add owner/test paths before editing. If already safe, record no change needed. Do not build a universal workflow engine.

| Order | Selected scope | Excluded from this pass |
| --- | --- | --- |
| P1 first | Clarify consequence/target for deployment, import, credential/account changes, removal, provider test, and recalculation where ambiguous (UI-02) | Uniform confirmation dialogs or cosmetic action redesign |
| P1 next | Check empty/invalid import submission, duplicate activation, and honest pending/failure/unknown outcomes; fix confirmed gaps (UI-03). Include UI-04 only for confirmed invalid persistence, lost drafts, or unsafe credential intent | A new progress API, richer import counts, tokenized inputs, or universal save bars |
| P1 next | Correct misleading Parse log failure styling and unsafe/unclear retry eligibility or feedback (UI-05 with UI-02/UI-03) | Rebuilding the whole log or dashboard |
| P2 | Concise diagnostics, supported dashboard drill-down, settings validation clarity/inherited defaults, and reliable freshness information | New endpoints merely to complete a visual candidate |
| P3 deferred | Admin table search/grouping, richer history summaries/filters, tokenized exclusions, and broad layout consistency | Not required for a P1/P2 exit |

An active data/operational hazard found in a later row takes precedence. Risky behavior was generally not exercised in the review: a missing observation is a verification task, not a proven defect. A visible enabled Import button alone does not establish that an empty request is sent. Deliver intent, validation, and lifecycle together for the selected operation, then run its focused check. No unrelated settings or screen prerequisite may delay it.

## UI-02: Clear Consequence and Target

**Priority / observation / sources:** P1 for unclear consequential intent/target; P3 for appearance-only consistency. High observations in [System](ui-design-improvements-administration.md#system), [Import Ratings](ui-design-improvements.md#import-ratings), [OMDb Provider](ui-design-improvements-administration.md#omdb-provider), [Users](ui-design-improvements-administration.md#users), [Ignored Titles](ui-design-improvements.md#ignored-titles), and [Recommendation Settings](ui-design-improvements-administration.md#recommendation-settings); Medium ambiguous Torrent feed removal and contextual Parse log retry; Low Favorites icon clarity.

**Impact / affected areas:** Prevent wrong-target and misunderstood actions across deployment, import, provider testing, account status, ignored/favorite lists, feeds, retries, and recalculation. Apply common naming/confirmation conventions, but keep the operation's own decision owner.

**Dependencies:** Confirm actual effects, reversibility, API permissions, quota use, import merge semantics, and target identifiers. The review reports persisted vote-weight recalculation is movie-specific while series calculation is request-time; verify this contract before publishing scope claims. Deployment target information may not exist before a check.

**Confidence / questions:** High confidence that reviewed labels/context are ambiguous, not that any operation is implemented incorrectly. OMDb quota/draft-key behavior and deployment preview availability are unknown.

**Acceptance:**

- Each action names its effect and target. Repeated row controls have accessible names containing non-sensitive context sufficient to distinguish their targets; account context is displayed only in the authorized local UI, never retained in review artifacts.
- Ignored-list wording explicitly means stop ignoring if the contract confirms that behavior; feed removal identifies the URL/profile; favorite removal remains distinct from ignoring.
- Deployment explains that checking can deploy, shows current and target context where known, explicitly marks unknown target information, and requests confirmation before deployment. Account deactivation, credential clearing, and feed removal make target/consequence explicit; use proportionate confirmation where ambiguity or irreversibility warrants it, not a blanket modal requirement for every action or routine save.
- Import rules are readable before submission: add/update matching IMDb IDs and retain absent ratings if confirmed. Recalculation states movie/series scope accurately. Provider test states credential source and request/quota effect before activation without exposing keys.
- UI warnings do not imply that sign-in supplies an authorization boundary; current network/access guidance remains visible beside sensitive actions.

## UI-03: Valid Submission and Honest Operation Lifecycle

**Priority / observation / sources:** P1 for confirmed unsafe submission, duplicate activation, or misleading/absent operation outcomes; P2 for richer supported summaries/freshness. [Import Ratings](ui-design-improvements.md#import-ratings) shows an enabled empty-file submission (High) and unreviewed lifecycle. Medium lifecycle/feedback candidates recur in System, Torrent Settings, Recommendation Settings, OMDb Provider, Dashboard, and Parse Log in the [administration backlog](ui-design-improvements-administration.md).

**Impact / affected areas:** Reduce uncertain outcomes, duplicate operations, and loss of editing context. Include list mutations where the shared action pattern is adopted; their pending/error behavior was not observed.

**Dependencies:** Establish synchronous versus queued behavior, authoritative operation/status IDs, supported counts/progress, duplicate-request behavior, and post-timeout recovery reads. A statistics refresh or provider test must not be assumed side-effect-free.

**Confidence / questions:** High confidence in the empty-file control state; lifecycle defects are not established because actions were not run. Counts, cancellation, progress percentages, and safe retries are unverified capabilities.

**Acceptance:**

- No import request is sent without one valid CSV within the stated 1 MiB limit; filename/type/size errors are associated with the field before submission. Cover boundary size, unsupported type, parse errors, and empty selection using fixtures.
- Every in-scope operation has explicit idle, pending, success, failure, and uncertain-outcome handling where applicable. Disable duplicate submission while pending; announce state changes without moving focus unnecessarily.
- Do not equate accepted/queued with completed or show invented progress percentages. Distinguish scan from recheck, and explain whether retry/recalculation affects one record or a wider dataset.
- On failure, retain valid non-secret input where safe and provide a specific recovery action. On timeout, report an unknown outcome and use an available status read before offering another mutation; do not retry automatically.
- P1 success feedback reports the available truthful result, never fabricated counts. Richer response-supported added/updated/unchanged/rejected summaries and last-run/freshness presentation are P2; labels distinguish successful completion from the latest attempt, including date and time zone.
- Local fixture tests cover successful completion, validation failure, service failure, duplicate activation, and uncertain outcome for each changed workflow; no deployed operation is used for validation.

## UI-04: Settings Validation and Persisted-State Clarity

**Priority / observation / sources:** P1 only for confirmed invalid persistence, draft loss, or unsafe credential intent; P2 for validation/default-state clarity; P3 for input replacement and long-table navigation. [Torrent Settings](ui-design-improvements-administration.md#torrent-settings) free-text exclusions are High; unsaved/save states, recommendation override ambiguity/navigation, and OMDb credential transitions are Medium. Free text alone is not evidence of unsafe saving.

**Impact / affected areas:** Prevent malformed settings and confusion between draft, inherited, and saved values. Functional owners remain Torrent matching rules, global recommendation settings, and provider credentials separately.

**Dependencies:** Confirm permitted genre/country values, legacy values, normalization, validation contracts, and whether saving triggers calculation. Reuse an existing appropriate input component if available; tokens/multi-select are candidate treatments, not assumed approved controls.

**Confidence / questions:** High confidence in observed input/disabled-field presentation; no evidence that malformed values are accepted by the server. Supported option vocabularies and existing invalid-value policy need checking.

**Acceptance:**

- Unsupported, duplicate, and malformed exclusions receive field-level feedback; existing values are not silently dropped or changed during editing. Retain free text if validation suffices; tokenization/multi-select is P3 and needs a canonical-value/legacy-value policy before approval.
- Save availability, unsaved edits, validation, pending state, success, and failure remain discoverable throughout long forms. A saved timestamp changes only after confirmed persistence; failures preserve safe draft values.
- Inherited defaults and active overrides are explicitly labeled, visually distinct beyond color, and correctly disabled/operable. Category search/source grouping is P3; if later approved, it must retain draft edits and hidden validation errors.
- OMDb distinguishes keep-existing, pending replacement, and clear-existing intentions. Stored credentials are never revealed; prevent secret values in feedback, logs, or artifacts and do not echo a replacement on completion.
- Save controls and table headers do not cover rows or focus; each edited control has an associated label/error and keyboard-operable interaction.

## UI-05: Diagnostic Meaning and Useful Next Steps

**Priority / observation / sources:** P1 for misleading failure/retry semantics; P2 for concise diagnostics, supported drill-down, and freshness; P3 for richer histories and table layout/navigation. [Parse Log](ui-design-improvements-administration.md#parse-log) uses green treatment for different failed outcomes and tall diagnostic rows (High); [Dashboard](ui-design-improvements-administration.md#dashboard) has static Problem/Not Found summaries (High). Medium candidates include System log summaries, dashboard table/freshness clarity, and OMDb failure/history scanning.

**Impact / affected areas:** Improve triage without encouraging unsupported retries. Shared status/disclosure semantics may be reusable; parser outcomes, provider failures, deployment results, and enrichment statuses are distinct domains.

**Dependencies:** Verify each status enum and retry eligibility; identify existing filtered-record/job destinations. Define summary fields without retaining raw logs. UI-02/UI-03 control retry intent and lifecycle; Phase 3 owns generic result position.

**Confidence / questions:** High confidence in visible semantics and scan burden. Dashboard destinations may not exist, and green styling alone does not establish backend classification. The two Parse log entry points' equivalence is unverified.

**Acceptance:**

- Text plus semantic visual treatment distinguishes successful, pending, retryable, rejected, and terminal cases according to each verified domain; a failure is never presented as success solely through shared styling.
- Parse rows lead with parsed title and concise outcome/reason; raw title and full trace remain available through keyboard-operable disclosure. Retry is offered only for confirmed eligible states and has row-specific pending/result feedback.
- P2 dashboard drill-down uses an existing correctly filtered diagnostic destination if available. If absent, record the limitation rather than require a new endpoint to close the slice; no fake link or unsupported recovery action.
- Dashboard uses human-readable entities before technical table names and a full generated-at date/time/zone. Compact breakdowns or section navigation retain counts, shares, and semantic column headers.
- System shows a concise latest event/result before optional raw output. OMDb separates quota and authentication failures from successes using text/status cues, with contract-accurate next steps and a compact summary; history filtering is added only if approved and supported.

## Validation and Exit

Use the [common validation gate](ui-design-improvements-plan-validation.md) plus synthetic long category names, expanded logs, sparse/all-error histories, credential keep/replace/clear drafts, and off-screen validation errors. Test deployed-contract assumptions locally with stubbed providers, never real keys or production mutations. Focus web checks on each changed owner; affected backend contracts require their unit/integration gates through the repository runner.

Close only the approved problem when its relevant intent, validation, lifecycle, and recovery criteria pass. Record other candidates as deferred; do not require all workflows, diagnostic links, settings improvements, or P3 features before moving to the next priority. Preserve safety/accessibility checks for every touched control. Unsupported API additions require separate approval.