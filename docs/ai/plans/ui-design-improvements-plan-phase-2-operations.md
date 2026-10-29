# Phase 2: Safe Operations and Diagnostics

[Master plan and phase index](ui-design-improvements-plan.md)

## Entry and Implementation Order

Current status belongs in the [progress tracker](ui-design-improvements-plan-progress.md). Use Phase 1 offsets for sticky controls; non-layout safety work may proceed independently after approval. Verify each operation's request, persistence, status reads, errors, and direct consumers locally; add actual owner/test paths before editing. Do not create a universal job abstraction: deployment, CSV parsing, settings writes, and background jobs may have different owners.

Deliver UI-02 intent with UI-03 lifecycle for one workflow at a time: deployment; ratings import; credential/account/feed changes; scan/retry/recalculation/provider test; remaining settings/statistics operations. Include UI-04 validation with the relevant save slice and UI-05 diagnostic semantics with the relevant job/retry slice. Test each coherent slice before the next.

## UI-02: Clear Consequence and Target

**Priority / observation / sources:** P1; High observations in [System](ui-design-improvements-administration.md#system), [Import Ratings](ui-design-improvements.md#import-ratings), [OMDb Provider](ui-design-improvements-administration.md#omdb-provider), [Users](ui-design-improvements-administration.md#users), [Ignored Titles](ui-design-improvements.md#ignored-titles), and [Recommendation Settings](ui-design-improvements-administration.md#recommendation-settings); Medium ambiguous Torrent feed removal and contextual Parse log retry; Low Favorites icon clarity.

**Impact / affected areas:** Prevent wrong-target and misunderstood actions across deployment, import, provider testing, account status, ignored/favorite lists, feeds, retries, and recalculation. Apply common naming/confirmation conventions, but keep the operation's own decision owner.

**Dependencies:** Confirm actual effects, reversibility, API permissions, quota use, import merge semantics, and target identifiers. The review reports persisted vote-weight recalculation is movie-specific while series calculation is request-time; verify this contract before publishing scope claims. Deployment target information may not exist before a check.

**Confidence / questions:** High confidence that reviewed labels/context are ambiguous, not that any operation is implemented incorrectly. OMDb quota/draft-key behavior and deployment preview availability are unknown.

**Acceptance:**

- Each action names its effect and target. Repeated row controls have accessible names containing non-sensitive context sufficient to distinguish their targets; account context is displayed only in the authorized local UI, never retained in review artifacts.
- Ignored-list wording explicitly means stop ignoring if the contract confirms that behavior; feed removal identifies the URL/profile; favorite removal remains distinct from ignoring.
- Deployment explains that checking can deploy, shows current and target context where known, explicitly marks unknown target information, and requests confirmation before deployment. Account deactivation, credential clearing, and feed removal make the target/consequence clear in a proportionate confirmation; routine saves need not gain a redundant modal.
- Import rules are readable before submission: add/update matching IMDb IDs and retain absent ratings if confirmed. Recalculation states movie/series scope accurately. Provider test states credential source and request/quota effect before activation without exposing keys.
- UI warnings do not imply that sign-in supplies an authorization boundary; current network/access guidance remains visible beside sensitive actions.

## UI-03: Valid Submission and Honest Operation Lifecycle

**Priority / observation / sources:** P1; [Import Ratings](ui-design-improvements.md#import-ratings) shows an enabled empty-file submission (High) and unreviewed lifecycle. Medium lifecycle/feedback candidates recur in System, Torrent Settings, Recommendation Settings, OMDb Provider, Dashboard, and Parse Log in the [administration backlog](ui-design-improvements-administration.md).

**Impact / affected areas:** Reduce uncertain outcomes, duplicate operations, and loss of editing context. Include list mutations where the shared action pattern is adopted; their pending/error behavior was not observed.

**Dependencies:** Establish synchronous versus queued behavior, authoritative operation/status IDs, supported counts/progress, duplicate-request behavior, and post-timeout recovery reads. A statistics refresh or provider test must not be assumed side-effect-free.

**Confidence / questions:** High confidence in the empty-file control state; lifecycle defects are not established because actions were not run. Counts, cancellation, progress percentages, and safe retries are unverified capabilities.

**Acceptance:**

- No import request is sent without one valid CSV within the stated 1 MiB limit; filename/type/size errors are associated with the field before submission. Cover boundary size, unsupported type, parse errors, and empty selection using fixtures.
- Every in-scope operation has explicit idle, pending, success, failure, and uncertain-outcome handling where applicable. Disable duplicate submission while pending; announce state changes without moving focus unnecessarily.
- Do not equate accepted/queued with completed or show invented progress percentages. Distinguish scan from recheck, and explain whether retry/recalculation affects one record or a wider dataset.
- On failure, retain valid non-secret input where safe and provide a specific recovery action. On timeout, report an unknown outcome and use an available status read before offering another mutation; do not retry automatically.
- Import success reports only response-supported added/updated/unchanged/rejected counts, otherwise the available truthful summary. Last-run and freshness labels distinguish successful completion from the user's latest attempt, including date and time zone.
- Local fixture tests cover successful completion, validation failure, service failure, duplicate activation, and uncertain outcome for each changed workflow; no deployed operation is used for validation.

## UI-04: Settings Validation and Persisted-State Clarity

**Priority / observation / sources:** P1; [Torrent Settings](ui-design-improvements-administration.md#torrent-settings) free-text exclusions are High; unsaved/save states, recommendation override ambiguity/navigation, and OMDb credential transitions are Medium.

**Impact / affected areas:** Prevent malformed settings and confusion between draft, inherited, and saved values. Functional owners remain Torrent matching rules, global recommendation settings, and provider credentials separately.

**Dependencies:** Confirm permitted genre/country values, legacy values, normalization, validation contracts, and whether saving triggers calculation. Reuse an existing appropriate input component if available; tokens/multi-select are candidate treatments, not assumed approved controls.

**Confidence / questions:** High confidence in observed input/disabled-field presentation; no evidence that malformed values are accepted by the server. Supported option vocabularies and existing invalid-value policy need checking.

**Acceptance:**

- Unsupported, duplicate, and malformed exclusions receive field-level feedback; existing values are not silently dropped or changed during editing. Agree a canonical-value/legacy-value policy before replacing free text.
- Save availability, unsaved edits, validation, pending state, success, and failure remain discoverable throughout long forms. A saved timestamp changes only after confirmed persistence; failures preserve safe draft values.
- Inherited defaults and active overrides are explicitly labeled, visually distinct beyond color, and correctly disabled/operable. Category search or source grouping retains draft edits and hidden validation errors.
- OMDb distinguishes keep-existing, pending replacement, and clear-existing intentions. Stored credentials are never revealed; prevent secret values in feedback, logs, or artifacts and do not echo a replacement on completion.
- Save controls and table headers do not cover rows or focus; each edited control has an associated label/error and keyboard-operable interaction.

## UI-05: Diagnostic Meaning and Useful Next Steps

**Priority / observation / sources:** P1; [Parse Log](ui-design-improvements-administration.md#parse-log) uses green treatment for different failed outcomes and tall diagnostic rows (High); [Dashboard](ui-design-improvements-administration.md#dashboard) has static Problem/Not Found summaries (High). Medium candidates include System log summaries, dashboard table/freshness clarity, and OMDb failure/history scanning.

**Impact / affected areas:** Improve triage without encouraging unsupported retries. Shared status/disclosure semantics may be reusable; parser outcomes, provider failures, deployment results, and enrichment statuses are distinct domains.

**Dependencies:** Verify each status enum and retry eligibility; identify existing filtered-record/job destinations. Define summary fields without retaining raw logs. UI-02/UI-03 control retry intent and lifecycle; Phase 3 owns generic result position.

**Confidence / questions:** High confidence in visible semantics and scan burden. Dashboard destinations may not exist, and green styling alone does not establish backend classification. The two Parse log entry points' equivalence is unverified.

**Acceptance:**

- Text plus semantic visual treatment distinguishes successful, pending, retryable, rejected, and terminal cases according to each verified domain; a failure is never presented as success solely through shared styling.
- Parse rows lead with parsed title and concise outcome/reason; raw title and full trace remain available through keyboard-operable disclosure. Retry is offered only for confirmed eligible states and has row-specific pending/result feedback.
- Each dashboard problem summary either reaches an existing correctly filtered diagnostic destination or explains that no drill-down is currently available. Missing endpoints require separate approval, not a fake link or unsupported recovery action.
- Dashboard uses human-readable entities before technical table names and a full generated-at date/time/zone. Compact breakdowns or section navigation retain counts, shares, and semantic column headers.
- System shows a concise latest event/result before optional raw output. OMDb separates quota and authentication failures from successes using text/status cues, with contract-accurate next steps and a compact summary; history filtering is added only if approved and supported.

## Validation and Exit

Use the [common validation gate](ui-design-improvements-plan-validation.md) plus synthetic long category names, expanded logs, sparse/all-error histories, credential keep/replace/clear drafts, and off-screen validation errors. Test deployed-contract assumptions locally with stubbed providers, never real keys or production mutations. Focus web checks on each changed owner; affected backend contracts require their unit/integration gates through the repository runner.

Exit when each approved workflow's intent, validity, pending/completion/uncertainty behavior, and failure recovery meet its criteria; diagnostic links and classifications are verified rather than inferred. Defer unsupported API additions explicitly.