# UI Design Improvements: Remaining Work

**Status date:** 2026-10-30

## Purpose and Evidence

This document consolidates the unresolved and unverified items from the earlier UI design reviews. These are candidates for focused review, not blanket implementation approval. Verify each behavior against local source and fixtures before treating an observation as a defect or changing a contract.

The original screen reviews were performed on 2026-10-29, primarily at 1458 x 816. On 2026-10-30, the local browser was checked at 1458 x 816 on Catalog and Oscar only. The Catalog still showed only the tops of five-column poster cards in the first viewport. Other routes and deployment state were not reverified in that pass. No personal data or screenshots are included here.

## Completed Work Excluded

- UI-01 moved the conditional access warning into document flow, named the site navigation landmark, corrected selected page context, and improved narrow navigation behavior. Actual 200% zoom verification remains open.
- UI-02 clarified ignored-title actions and accessible names. Other consequential operations were not assessed.
- UI-08 corrected Favorites context and empty-state wording for mixed movie and series content.
- UI-10 added source-separated Oscar and Golden Globes category disclosures with category, outcome, and ceremony context. The local Oscar view showed these disclosures.

## P1: Verification and Operational Safety

- **UI-01, shell:** Verify the conditional access warning at actual 200% browser zoom, including focus and content overlap. The warning was absent in the available local browser session, so its live overlap behavior was not verified.
- **UI-02, action intent:** Verify the target and consequence for deployment, ratings import, account status changes, feed removal, provider tests, credential clearing, recalculation, and retry actions. Confirm quota, scope, and reversibility from the actual contracts before promising effects.
- **UI-03, submission and lifecycle:** Confirm that invalid or absent import files cannot submit; inspect type/size validation, duplicate activation, pending/result feedback, and uncertain outcomes. Check operation lifecycle states only where supported; do not imply unprovided progress or result counts.
- **UI-04, settings state:** Verify exclusion validation and preservation of existing values, saved versus draft state, inherited recommendation defaults versus active overrides, and OMDb keep/replace/clear credential intent. Do not expose stored credentials.
- **UI-05, diagnostics:** Verify Parse Log status meaning and retry eligibility; distinguish retryable, rejected, and terminal outcomes, provide concise diagnostics and row-specific retry feedback, and use existing dashboard destinations only. Do not invent links or recovery behavior.

## P2: Browsing and Domain Meaning

- **Catalog and award catalogs:** Improve first-viewport access to title and primary signals. Evaluate card width and metadata hierarchy at more than one desktop width; descriptions and award details remain dense. Make Table mode more compact without losing access to secondary details, and keep result context and column labels available during long scans. Preserve a legible missing-poster treatment and avoid repetitive unavailable metadata. The catalog heading and navigation currently say "Torents catalog"; correct both to "Torrents catalog".
- **Movie and series recommendations:** Make recommendation score prominent and distinct from IMDb rating; make the existing scoring explanation discoverable and state only verified per-title signals. Keep weight values, total, validity, keyboard behavior, and ranking context clear. Recheck first-viewport density. Hide unavailable director values; show series spans or lifecycle only when reliable data exists.
- **Personal ratings:** Add title lookup if supported; distinguish personal and IMDb ratings consistently; clarify the actual MediaDock-only inclusion rule and supported date provenance. Keep IMDb identifiers available as secondary details.
- **Favorites:** Bring watch/download state nearer the title, review layout for small collections, use consistent count wording, and omit useless pagination controls on a single page. Keep the poster action's accessible purpose distinct from removing a favorite. The mixed-content wording is already corrected.
- **Ignored titles:** Validate shared card sizing and action spacing at narrower widths. The action wording and title-specific accessible names are already corrected.
- **Ratings import:** Make the confirmed merge behavior easy to see before submission; provide readable export steps and CSV examples. Verify file selection, validation, progress, completion, and recoverable errors against supported response data before adding lifecycle summaries.
- **Oscar and Golden Globes:** Category details are implemented. Verify whether existing filters provide category discovery before proposing more controls. Golden Globes wording should reflect mixed movie and series results, not only films. Review result position, pagination context, and card density; keep page-size or page-jump controls conditional on demonstrated need and support.

## P2: Administration

- **System:** Clarify what manual check/deploy will do, target context where known, and confirmation and operation outcomes. Summarize deployment events before optional raw output, reduce competing scroll regions, and keep relevant security guidance near the controls.
- **Dashboard:** Make problem summaries actionable only through existing destinations; show an unambiguous generated timestamp and refresh outcomes; improve long-page scanning and prioritize human-readable entity names.
- **Torrent settings:** Validate exclusion values and preserve existing entries; show scan/recheck lifecycle and saved-versus-draft feedback; make feed-removal targets explicit.
- **Recommendation settings:** Explain recalculation scope accurately; distinguish inherited values from overrides; keep save and result state accessible during long tables. Improve category navigation only when supported.
- **OMDb provider:** Clarify provider-test credential source and quota effect; make credential drafts explicit without revealing secrets; improve quota/authentication failure cues and history scanning.
- **Users:** Give account actions target-specific accessible names and clear consequences; keep access-scope guidance visible and validate table operation at narrower widths.
- **Parse Log:** Keep status semantics, concise diagnostics, retry eligibility and feedback, and result context clear. The catalog History entry point also needs accurate page context; its relationship to the administration entry point remains to be verified.

## Deferred Candidates

These are not required to close an individual P1 or P2 finding and need separate need and scope review: new page-size or page-jump controls; additional filters such as series lifecycle or season count; tokenized settings inputs; richer numerical recommendation contributions requiring new data; new award or diagnostic API fields; and aesthetic-only card or layout changes.

## Working Rules

- Select one highest-priority unblocked finding at a time. Verify uncertain behavior locally before scheduling a fix; an unverified observation is not a confirmed defect.
- Preserve existing query, ranking, mutation, ownership, and security semantics. Do not remove or weaken the access warning.
- Use synthetic/local fixtures and the repository test runner for implementation checks. Do not test by mutating deployed data or credentials.
- Keep P3 items deferred while approved, unblocked P1/P2 work remains. Record unavailable data or contracts as blockers rather than inventing UI behavior.