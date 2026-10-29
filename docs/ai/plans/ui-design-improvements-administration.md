# Administration UI Design Improvement Backlog

## Purpose

This document keeps administration screen reviews separate from the [general UI design improvement backlog](ui-design-improvements.md). Items are visual-review candidates, not approved implementation scope.

## Review Constraints

- Reviews are observational only. Do not trigger actions that change settings, credentials, jobs, or other application state.
- Do not record personal account details, credentials, API keys, raw operational logs, or screenshots from authenticated sessions.

## System

- **Route:** [`/configuration/system`](https://media.valyo.eu/configuration/system)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; system version and deployment status; recent deployment output expanded.
- **Visible state:** Automatic checks every five minutes; latest deployment check succeeded.
- **Primary task:** Review the running version and deployment status, or request a manual check/deploy.
- **Review method:** Visual and read-only page inspection only; no deployment or settings actions were activated. Account details and raw deployment output were not recorded.

### What Works

- Version, commit, and deployment timestamps are available for release identification.
- The deployment section shows its automatic-check cadence, latest result, live commit, and recent output.
- The shared access-control warning and a system-specific security note are present.

### Candidate Improvements

1. **High - Make the manual deployment action unmistakable.** The prominent button combines "Check and deploy now" beside the automatic-check status. Clarify what will happen, show the target/current version context, and make confirmation plus in-progress, success, and failure states clear before the operation starts.
2. **Medium - Summarize before showing raw deployment output.** The expanded log occupies a large, independently scrollable area and exposes only part of its contents at once. Lead with a concise event summary and keep the raw output in a clearly expandable viewer.
3. **Medium - Simplify the nested scrolling.** The page, administration sidebar, and log each scroll independently; the sidebar also extends beyond the viewport. Reduce competing scroll regions and keep the active administration destinations easy to discover.
4. **Medium - Give system-specific security guidance stronger hierarchy.** The detailed warning appears below the deployment log in subdued text, while the top banner carries the broader access warning. Keep the essential guidance visible near the related controls without repeating the full notice.

## Dashboard

- **Route:** [`/configuration/dashboard`](https://media.valyo.eu/configuration/dashboard)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; system-table overview and all detail tables expanded.
- **Visible state:** Summary timestamp shown; IMDb, Golden Globes, and Oscar breakdowns; refresh idle.
- **Primary task:** Monitor title-table coverage and metadata-enrichment health.
- **Review method:** Visual and read-only accessibility inspection, including scrolling; Refresh statistics was not activated. Exact aggregate values and account details were not recorded.

### What Works

- The overview table compares total, enriched/linked, pending, and nomination counts across the three source tables.
- Detail tables separate media or nominee types from enrichment status, and show both counts and shares.
- Semantic table headers make the data relationships clear, while the summary time and refresh control are easy to find.

### Candidate Improvements

1. **High - Make problem statuses actionable.** Non-zero Problem and Not Found rows are static summaries with no visible path to affected records or diagnostics. Link these states to relevant filtered records, jobs, or troubleshooting views where available.
2. **Medium - Make data freshness unambiguous.** The summary label shows a time without a date or time zone. Show the full generation time and provide clear loading, success, and failure feedback when refreshing.
3. **Medium - Reduce the long vertical scan.** The three detail sections form a long page with generous table-row spacing. Compact repeated breakdowns or add section navigation/collapsible detail while keeping the top-level summary visible.
4. **Medium - Put human-readable names first.** Internal table identifiers recur in overview rows and section headings. Keep the entity name primary and show the database identifier as secondary technical context.
5. **Shared - Prevent the global alert from covering content.** While scrolling, the fixed access notice overlaps the current section's introductory text. Apply the shared alert/scroll-offset treatment noted for Catalog and Golden Globes here as well.

## Torrent Settings

- **Route:** [`/configuration/torrent`](https://media.valyo.eu/configuration/torrent)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; RSS profiles, scan controls, and matching settings.
- **Visible state:** Three named RSS profiles; no active ingestion job; matching settings and Save control visible after scrolling.
- **Primary task:** Manage RSS feed profiles and matching rules, then start or recheck a scan.
- **Review method:** Visual and read-only accessibility inspection, including scrolling; no feed, scan, or settings actions were activated. Feed URLs and configuration values were not recorded.

### What Works

- RSS feeds are grouped by their system profile, with Add URL and per-feed Replace/Remove actions.
- Start scan and Recheck failed are grouped with a visible active-job status.
- Matching settings separate movie and series rating thresholds, and show a last-updated timestamp beside Save settings.

### Candidate Improvements

1. **High - Make exclusion entry structured and validatable.** Genres and countries are entered as comma-separated free text. Use tokenized or multi-select inputs and validate unsupported, duplicate, or malformed values before saving.
2. **Medium - Show the scan lifecycle beside its actions.** The current view reports whether an ingestion job is active, but no last-run summary is visible. Present the latest scan time/result and clear running, completed, and failed states; distinguish a new scan from retrying failed entries.
3. **Medium - Make feed removal intent explicit.** The visible per-feed action is labeled only "Remove." Use wording such as "Remove URL" and ensure the affected profile/feed is clear before confirming removal.
4. **Medium - Clarify unsaved and saved settings states.** Show validation and unsaved-change state as users edit, then provide clear save success or failure feedback. Keep the last-updated timestamp tied to the persisted settings.
5. **Shared - Prevent the global alert from covering profile content.** While scrolling, the fixed access notice overlaps the final RSS profile row. Apply the shared alert/scroll-offset treatment already noted for the catalog and dashboard screens.

## Recommendation Settings

- **Route:** [`/configuration/recommendations`](https://media.valyo.eu/configuration/recommendations)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; freshness notice, scoring defaults, category overrides, and save control.
- **Visible state:** New-metadata warning; category overrides listed by source; Save is below the long table.
- **Primary task:** Configure global recommendation normalization and award points, including source/category overrides.
- **Review method:** Visual and read-only accessibility inspection, including scrolling; no weights, overrides, or recalculation actions were changed. Exact settings values were not recorded.

### What Works

- The page distinguishes global calculation settings from personal score coefficients, which are configured on the recommendations page.
- Vote-weight freshness and the new-metadata warning are shown near the recalculation action.
- Category overrides use explicit source, category, override, win-points, and nomination-points columns.

### Candidate Improvements

1. **High - Explain recalculation scope and impact.** The warning says new metadata may make scores inaccurate, but does not clarify that persisted vote-weight recalculation applies to movies while series weights are calculated at request time. Make the scope and expected effect clear, and show running, success, and failure feedback.
2. **Medium - Make category overrides easier to navigate.** The long table has lengthy category names, no visible search or source filter, and no sticky column headings while scrolling. Add category search or source grouping and keep column context available.
3. **Medium - Distinguish inherited defaults from active overrides.** Unchecked rows still display numeric values in fields that are disabled, and those fields look similar to editable values. Label them as inherited defaults and make disabled state visually unmistakable.
4. **Medium - Keep save state accessible throughout the table.** Save recommendation settings appears only after the long override list. Keep the action and unsaved-change state available while editing, with clear validation and save-result feedback.
5. **Low - Remove the repeated page heading.** "Recommendation settings" appears as both the page title and the main section heading. Let one heading identify the screen and use the other position for a distinct group label or remove it.
6. **Shared - Prevent the global alert from covering override rows.** While scrolling, the fixed access notice overlays the table content. Apply the shared alert/scroll-offset treatment already noted for the catalog and administration dashboard.

## OMDb Provider

- **Route:** [`/configuration/omdb`](https://media.valyo.eu/configuration/omdb)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; provider settings and the last 30 UTC days of request history.
- **Visible state:** Saved-credential status, shared daily request limit, Save/Test actions, and request-history statuses.
- **Primary task:** Manage provider credentials and the shared request limit, test connectivity, and review request usage.
- **Review method:** Visual and read-only inspection, including scrolling; no key, save, clear, or test action was activated. No credentials, account details, exact request counts, or raw values were recorded.

### What Works

- The saved credential is represented by a status without exposing its value; the empty field explains that it preserves the saved key, with a separate clear option.
- The quota explanation states that RSS and Oscar share the limit.
- The history table uses UTC dates and separates totals, RSS, Oscar, and status for the recent period.

### Candidate Improvements

1. **High - Make the provider test's effect explicit.** The Test OMDb action sits beside Save and names a fixed title, but its request and quota impact are not clear. Explain whether it consumes the shared quota and show a safe result/error state without exposing the credential.
2. **Medium - Make credential changes unmistakable.** Preserve the helpful blank-to-keep behavior, but clearly show when a replacement key is pending save and what clearing the saved key will do. Never display the stored value.
3. **Medium - Make quota and authentication failures scannable.** The history shows quota-blocked and authentication-error statuses as plain text alongside successful days. Use clear text-and-status treatments and concise next-step guidance for failures.
4. **Medium - Shorten the request-history scan.** The 30-day table makes the page several viewports tall. Add a compact usage summary and a way to filter by source or status, while keeping the settings context easy to return to.
5. **Low - Remove the repeated page heading.** "OMDb provider" appears as both the page title and the main section heading. Use one heading for the screen and give the section a distinct label if needed.
6. **Shared - Keep the global alert clear of history rows.** The fixed access notice covers the top of the table while scrolling. Apply the shared alert/scroll-offset treatment noted on the other administration and catalog screens.

## Users

- **Route:** [`/configuration/users`](https://media.valyo.eu/configuration/users)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; account-status filter, search, and users table.
- **Visible state:** All users view; account-row details intentionally omitted.
- **Primary task:** Review registration requests and manage account status.
- **Review method:** Visual and read-only accessibility inspection only; no search, filters, or account actions were activated. No account names, emails, roles, or status values were recorded.

### What Works

- The status filter and name/email search are grouped above the results.
- The table separates account, status, role, registration request, and actions into labeled columns.
- Account status has a distinct visual label, and the available row action is visible.

### Candidate Improvements

1. **High - Make account actions contextual and accessible.** The Deactivate button has a generic accessible name. Include the affected account in its accessible name and make the target and consequence clear in any confirmation before changing status.
2. **Medium - Keep the trust boundary visible near actions.** The screen notes that user-management actions are available within the configured network boundary, but the helper text is subdued. Keep the current access scope easy to see beside account-changing controls.
3. **Low - Remove the repeated page heading.** "Users" appears as both the page title and the main section heading. Use one heading for the screen and give the section a distinct label if needed.
4. **Low - Verify the table at narrower widths.** The reviewed desktop table has five columns and account identity details. Check that columns and row actions remain readable and operable without awkward wrapping or horizontal clipping on smaller screens.

## Parse Log

- **Route:** [`/history`](https://media.valyo.eu/history)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Parse log tab; Failed outcome; page 1 of 3.
- **Visible state:** Title/feed search, outcome filter, diagnostic rows, and per-row Retry match actions.
- **Primary task:** Investigate parser/matching failures and retry eligible entries.
- **Review method:** Visual and read-only inspection, including scrolling; no search, filters, or Retry actions were activated. Raw titles, URLs, and row-specific data were not recorded.

### What Works

- Scan runs and Parse log are separate views, with outcome filtering and title/feed search available.
- The table exposes raw and parsed title context, feed/year, outcome details, processing type, attempts, and time.
- Retry match is available at the row where investigation is needed, and record count/pagination are shown at the end of the list.

### Candidate Improvements

1. **High - Distinguish failure outcomes clearly.** In the Failed view, no-result and rejected outcomes use the same green badge treatment. Use semantic status styling and text that separates retryable, rejected, and terminal cases without relying on color alone.
2. **High - Show concise diagnostics first.** Long raw release titles and stacked lookup/reason/decision details make rows tall and difficult to compare. Lead with the parsed title and a short outcome/reason, then allow the raw title and full diagnostic trace to expand on demand.
3. **Medium - Keep result context near the filters.** The record count and page position appear only below the long list. Show them beside the outcome/search controls and keep a compact return to the filters available while scrolling.
4. **Medium - Make Retry match contextual and stateful.** The same action label repeats for every row. Include the entry context in its accessible name and ensure in-progress and completion/error feedback prevents accidental duplicate retries.
5. **Shared - Keep the global alert clear of the log.** While scrolling, the fixed access notice overlaps the first visible data row. Apply the shared alert/scroll-offset adjustment noted under System and the catalog reviews.

### Alternate Entry Point: Catalog History

- **Route:** [`/catalog`](https://media.valyo.eu/catalog) with History > Parse log selected.
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Library > History > Parse log; Failed outcome.
- **Review method:** Read-only visual inspection; no tabs or filters changed. Row titles and details were not recorded.

#### Candidate Improvements

1. **Medium - Align page context with the selected view.** The page title and description still identify the Torrents catalog and browsing titles while History > Parse log is active. Use a history/parse-log heading or update the helper copy for the selected subview.
2. **Low - Clarify the relationship to Administration > Parse log.** The same view is available from the catalog History tab and the separate administration route. Explain the scope of each entry point or consolidate them to avoid duplicate navigation.