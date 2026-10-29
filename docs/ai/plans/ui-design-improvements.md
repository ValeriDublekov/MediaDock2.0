# UI Design Improvement Backlog

## Purpose

This document records screen-by-screen visual observations from the deployed MediaDock UI. Items are candidates for later review, not approved implementation scope. Keep implementation separate until the relevant screen and proposed changes are agreed.

## Review Constraints

- Reviews are observational only. Do not trigger actions that change favorites, ignored titles, ratings, catalog data, jobs, or other application state.
- Do not record personal account details or store screenshots from authenticated sessions here.

## Catalog

- **Route:** [`/catalog`](https://media.valyo.eu/catalog)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Titles > Movies > Posters; 144 titles shown.
- **Primary task:** Browse and filter titles collected from configured feeds.
- **Review method:** Visual inspection and scrolling only; no filters or result actions were activated.

### What Works

- Main and administration navigation are clearly grouped, with a visible active destination.
- Movie/series selection and poster/table view are easy to distinguish.
- Search, genre, year, and IMDb rating controls are available on the catalog screen.
- Result count and feed/award context are present.

### Candidate Improvements

1. **High - Bring results into the first viewport.** At the reviewed desktop size, the page heading, persistent alert, tabs, and filter controls leave only the tops of the poster cards visible. Tighten the vertical rhythm and make advanced filters more compact or collapsible, while keeping title search and result count easy to find.
2. **High - Give cards more room to scan.** The five-column grid makes titles, descriptions, and secondary details narrow; several descriptions are visibly truncated. Evaluate a four-column layout at this width or use a minimum card width with responsive column breakpoints. Check more than one desktop width before choosing.
3. **Medium - Establish a clearer card hierarchy.** Keep title, year, rating, and essential synopsis prominent. Give director, genres, award details, feed observation counts, and RSS timestamps a calmer secondary treatment without removing access to them.
4. **Medium - Make the missing-poster state intentional.** One visible card uses a plain dark placeholder with the title. Consider a consistent fallback treatment that preserves the poster aspect ratio and keeps the title legible.
5. **Medium - Clarify poster-overlay actions.** The card actions are icon-only and sit over the artwork. Ensure each has an accessible name, a clear hover/focus treatment, and does not obscure important poster content.
6. **Medium - Keep result context available during long scans.** The count and poster/table controls sit above the grid and disappear while scrolling. Consider a compact sticky result toolbar or a quick return to the filters; ensure it does not compete with or overlap the persistent alert.
7. **Medium - Rebalance the persistent alert.** The full-width alert has strong visual weight and occupies space above the catalog. Review its compactness and severity hierarchy, while keeping critical operational or security information visible and understandable.
8. **Low - Check navigation wrapping at narrower widths.** Several long sidebar labels wrap onto two lines. Verify consistent item alignment and active-state clarity at narrower desktop and mobile widths; those sizes were not reviewed in this pass.

### Alternate View: Table

- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Titles > Movies > Table; 144 records; page 1 of 3.
- **Review method:** Visual inspection and scrolling; switched from History to Titles and from Posters to Table only. No filters or result actions were used.

#### What Works

- Columns expose title, type, IMDb rating, awards, genres, RSS date, and actions for side-by-side comparison.
- Row actions are text-labeled, and the small poster thumbnail helps identify a title.

#### Candidate Improvements

1. **High - Make table mode genuinely compact.** Rows still include a poster thumbnail, director, and multi-line synopsis, so the view remains tall and requires substantial scrolling. Collapse secondary metadata or offer a denser row mode while preserving access to details.
2. **Medium - Reduce repeated empty award text.** "No linked nominations" repeats across rows in subdued text. Use a concise, clearly readable absence indicator so actual award data is easier to scan.
3. **Medium - Keep column context while scrolling.** The table header scrolls out of view on the long result list. Keep column labels available without allowing the global alert to cover the first data row.
4. **Medium - Keep result position near the table controls.** The record count and page position appear only at the bottom. Reuse the shared result-context treatment described above for the table view.

## Movie Recommendations

- **Route:** [`/account/recommendations/movies`](https://media.valyo.eu/account/recommendations/movies)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Posters; 586 recommendations shown.
- **Visible state:** Year range 1980-2026; all genres; watched-title exclusion checked; score weights 50% IMDb rating, 25% vote popularity, 25% award points.
- **Primary task:** Scan a personal ranking of eligible award-nominated movies and understand why each title is recommended.
- **Review method:** Visual inspection and scrolling only; no filters, score weights, watched state, or card actions were changed.

### What Works

- The page explains that recommendations are personalized and names the main input signals.
- Year, genre, and watched-title filters are grouped above the results.
- Score weights and their 100% total are visible, with a scoring explanation available.
- Cards include IMDb rating, award/nominations data, and a separate recommendation score.

### Candidate Improvements

1. **High - Make the recommendation score prominent.** The score is at the bottom of each card, separated from the title and IMDb rating. Bring it into the card's primary information area and use a distinct visual treatment so the two scores are not conflated.
2. **High - Explain why each title ranks here.** The general scoring explanation is collapsed, while individual cards show a score without its contribution breakdown. Make the explanation easier to discover and consider a concise per-title summary of the rating, popularity, and award signals that influenced its position.
3. **High - Bring useful result content into the first viewport.** The page title, repeated "Recommendations" heading, alert, filters, and score controls push card details below the fold. Review whether the repeated heading and vertical spacing can be reduced without hiding the score controls or their explanation.
4. **Medium - Make weight adjustment easier to reason about.** Keep each slider's current percentage and the total closely associated with the controls. Ensure the relationship between the three weights, the 100% total, and the resulting ranking is clear and keyboard accessible.
5. **Medium - Preserve ranking context while browsing.** With 586 results, make the current ordering explicit (for example, recommendation score descending) and keep the result count or a compact return to filters available during long scans.
6. **Medium - Reuse the catalog card improvements.** The same narrow five-column layout, truncated synopsis, and icon-only artwork actions appear here. Review a shared card width, metadata hierarchy, and accessible action treatment rather than tuning these separately per screen.
7. **Low - Review the shared alert once across screens.** The persistent full-width alert competes with the recommendation controls here as it does on the catalog. Treat its size and severity hierarchy as shared application-shell work; keep critical information visible.

## Series Recommendations

- **Route:** [`/account/recommendations/series`](https://media.valyo.eu/account/recommendations/series)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Posters; 293 recommendations shown.
- **Visible state:** Series-year range 1951-2025; all genres (26); watched-series exclusion checked; score weights 50% IMDb rating, 25% vote popularity, 25% award points.
- **Primary task:** Browse a personal ranking of eligible award-nominated series and judge which titles are worth exploring.
- **Review method:** Visual inspection and scrolling only; no filters, score weights, watched state, or card actions were changed.

### What Works

- Series-specific wording is used for the year filter and watched-title exclusion.
- Cards show IMDb ratings, Golden Globes award/nominations data, and recommendation scores.
- The familiar filter and score-control layout matches the movie recommendations screen.

### Candidate Improvements

1. **Medium - Replace repeated missing metadata with useful series context.** All visible cards show "Director: Not listed," which adds noise without helping users compare series. Hide unavailable values; where reliable data exists, consider showing season or episode count instead.
2. **Medium - Show the series lifecycle.** Cards display a single year, which does not tell users whether a series ran for multiple years or is still ongoing. Where the data is known, show a year span and an ended/ongoing state.
3. **Low - Evaluate series-specific filters.** If users still face too many choices, consider filters such as ongoing/ended or season count. Validate the need and data availability before adding controls.
4. **Shared with Movie Recommendations - Reuse the common improvements.** The first-viewport density, score placement and explanation, weight controls, five-column cards, and global alert are shared concerns. Address them in the common recommendations layout, then verify the series-specific labels and metadata here.

## Favorites

- **Route:** [`/favorites`](https://media.valyo.eu/favorites)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; All > Posters; 8 titles; page 1 of 1.
- **Visible scope:** The cards include both movies and series, although the breadcrumb and helper text refer to movies.
- **Primary task:** Review saved titles and manage their watch/download status.
- **Review method:** Visual inspection and scrolling only; no tabs, checkboxes, removal, poster actions, or pagination controls were activated.

### What Works

- All, To watch, and To download views are clearly grouped, with a poster/table switch.
- To watch and To download states have explicit text labels on each card.
- The Remove from favorites action is text-labeled rather than represented only by an icon.

### Candidate Improvements

1. **High - Align the page name and content scope.** The page is titled Favorites, its breadcrumb says "My movies," and the helper text describes movies, while the list contains both movies and series. Make the breadcrumb and helper copy reflect the actual mixed scope, or provide an explicit type filter if the page is intended to be movie-only.
2. **Medium - Bring watch/download status closer to the title.** The labeled checkboxes sit at the bottom of each card, after the synopsis and awards. Consider moving them nearer the card's primary information or showing a compact status summary there.
3. **Medium - Balance the grid for a small collection.** Eight titles produce a five-card first row and three-card second row, leaving a large unused area. Evaluate a lower maximum column count or a responsive minimum card width for small collections.
4. **Medium - Simplify single-page navigation.** The footer shows "8 records | Page 1 of 1" with disabled Previous and Next controls, while the header calls them titles. Use consistent count wording and omit pagination controls when there is only one page.
5. **Medium - Reduce repeated missing metadata.** Several visible cards show "Director: Not listed." Hide unavailable fields or use the space for another useful attribute when reliable data exists.
6. **Low - Clarify the poster action.** The crossed-eye icon has no visible text in the card view. Provide an accessible name and tooltip, and keep it visually distinct from the separate Remove from favorites action.

## Ignored Titles

- **Route:** [`/account/ignored-movies`](https://media.valyo.eu/account/ignored-movies)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Posters; 9 titles.
- **Visible scope:** The list includes both movies and a series; the page title uses the general term "Ignored titles."
- **Primary task:** Review ignored titles and manage whether they remain ignored.
- **Review method:** Visual inspection and scrolling only; no Remove buttons or other controls were activated.

### What Works

- The general page title accommodates both movies and series.
- The title count and poster/table view are easy to find.
- Cards show a title, year, IMDb rating, and date added for context.

### Candidate Improvements

1. **High - Clarify what "Remove" does.** On an ignored-title screen, "Remove" could mean deleting the title or removing it from the ignored list. Use an explicit action label, such as "Restore" or "Stop ignoring," if that is the intended behavior. The accessibility snapshot also exposes every card action with the same name "Remove" and no title; include the title in each accessible name.
2. **Low - Validate shared card sizing at smaller widths.** The five-column poster layout is consistent with other screens, and the simpler cards remain scannable at the reviewed desktop size. Check the shared minimum card width and action spacing on narrower viewports before changing this screen independently.

## Personal Ratings

- **Route:** [`/account/personal-ratings`](https://media.valyo.eu/account/personal-ratings)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Posters.
- **Primary task:** Find and review personal IMDb ratings, distinguishing ratings recorded in MediaDock from imported rating data.
- **Review method:** Visual inspection and scrolling only; no sorting, filters, imports, rating edits, or card actions were activated. Personal titles, rating values, and result counts were not recorded.

### What Works

- Sorting options cover rating and vote date, with a visible selected state.
- A filter is available for ratings added in MediaDock.
- Cards label the personal rating and IMDb rating separately, and a table view is available.

### Candidate Improvements

1. **High - Add a way to find a specific title.** No title search is visible alongside the sort and source controls. Consider a persistent title search, with optional year or rating filters, for browsing a large personal library.
2. **High - Separate the two rating signals more clearly.** "Your rating" and IMDb rating are both values on a ten-point scale, and the personal value sits over the poster while the IMDb value appears in the card details. Keep both labels explicit and give the personal rating a consistent, distinct place in the card hierarchy.
3. **Medium - Explain rating provenance.** Clarify what "Only show ratings added in MediaDock" includes and how those ratings differ from imported IMDb votes. Distinguish vote dates from dates a rating was imported or recorded.
4. **Medium - De-emphasize technical identifiers.** IMDb IDs appear as small technical text on cards. Keep them available for identification or copying, but move them out of the primary browsing hierarchy.
5. **Medium - Reuse shared card readability improvements.** The five-column layout and truncated metadata make the cards dense. Apply the shared card-width and metadata hierarchy guidance, and validate the table view as an efficient alternative for a large collection.
6. **Low - Reduce repeated page headings.** "Personal ratings" and "Personal IMDb ratings" repeat the same idea. Review whether one heading can carry the page context without losing the IMDb source information.

## Import Ratings

- **Route:** [`/account/personal-ratings/import`](https://media.valyo.eu/account/personal-ratings/import)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; no file selected.
- **Visible constraints:** One CSV at a time; `.csv`/`text/csv`; maximum file size 1 MiB.
- **Primary task:** Import an IMDb ratings export into the personal ratings library.
- **Review method:** Visual inspection and read-only control-state inspection; no file was selected and no import was started.

### What Works

- The accepted format and file-size limit are stated near the upload control.
- Instructions identify where to find the ratings CSV in an Amazon data archive, and a CSV format example is provided.
- The merge behavior is described: matching IMDb IDs are added or updated, while ratings absent from the file are retained.

### Candidate Improvements

1. **High - Prevent an empty import attempt.** The Import ratings button is enabled while no file is selected. Disable it until a valid CSV is chosen, or provide an equally clear inline validation state before submission.
2. **High - Bring merge behavior into the action hierarchy.** The add/update and preserve-missing-ratings rules affect user data, but currently sit in subdued helper text. Present them in a concise, readable summary beside the upload and before the import action.
3. **Medium - Design the full import lifecycle.** After file selection, show the selected filename and type/size validation. Provide clear parsing, progress, completion, and recoverable error states; summarize added, updated, unchanged, and rejected rows when the import response supports those counts.
4. **Medium - Make export instructions easier to follow.** The archive path is long and embedded in paragraph text. Present the steps and path as a scannable instruction, keeping the exact folder path easy to copy; format the CSV example as a small code block or table.
5. **Low - Improve the form's reading measure.** The upload field spans most of the content area while the instructions are short. Evaluate a comfortable maximum width for the form and its help text without introducing a decorative card.

## Oscar Catalog

- **Route:** [`/oscar`](https://media.valyo.eu/oscar)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Posters; 581 Oscar films; More filters collapsed.
- **Primary task:** Browse Oscar-nominated films and review category-level outcomes alongside separate provider metadata.
- **Review method:** Visual inspection and scrolling only; no search, filters, refresh, or card actions were activated. The contents of More filters were not inspected.

### What Works

- Film search, year and IMDb-rating ranges, result count, and poster/table views are available.
- Cards identify Oscar and Golden Globes totals in separately labeled rows.
- The page heading and description clearly identify the Oscar catalog.

### Candidate Improvements

1. **High - Bring Oscar category outcomes into focus.** The visible cards show aggregate Oscar wins and nominations, but not category names. Provide a clear, structured path from those totals to category-level nominations and wins, so users can scan which categories a film was nominated for or won.
2. **High - Keep Oscar facts distinct from general awards.** Both Oscar and Golden Globes totals sit under the generic "Awards and nominations" heading. Use distinct visual grouping and labels so Oscar category facts are not conflated with other award/provider metadata.
3. **Medium - Make Oscar-specific discovery easy to find.** Search, year, and IMDb rating are visible, while More filters is collapsed. Review whether category-based discovery or filtering is available there; if so, make that key Oscar workflow easier to discover.
4. **Medium - Improve first-viewport and card scan density.** The five-column grid begins at the bottom of the first viewport, and card descriptions and award details are narrow. Reuse the shared card-width guidance and compact the filter area enough to bring useful title and award context into view sooner.

## Golden Globes Catalog

- **Route:** [`/golden-globes`](https://media.valyo.eu/golden-globes)
- **Reviewed:** 2026-10-29
- **View:** Desktop, 1458 x 816; Posters; 1,270 records; page 1 of 22; More filters collapsed.
- **Visible state:** Ceremony-year range 1981-2026; IMDb-rating range 3.2-9.6.
- **Primary task:** Browse Golden Globes nominees and winners across movies and series, and review their award outcomes.
- **Review method:** Visual inspection and scrolling only; no search, filters, view modes, refresh, or result actions were activated.

### What Works

- Search, year and IMDb-rating ranges, result count, and poster/table view are easy to locate.
- Cards identify movies versus series and show the ceremony year, IMDb rating, genres, and Golden Globes totals.
- The result count and Previous/Next controls communicate progress through the paginated results.

### Candidate Improvements

1. **High - Align the catalog wording with its mixed content.** The search label and result count say "films," but the visible results include both movies and series. Use a neutral term such as "titles" or "nominees," or provide an explicit media-type filter if separate browsing is intended.
2. **High - Surface category-level award outcomes.** The page description promises nominees, winners, and award categories, while the cards show only aggregate wins and nominations. Provide a scannable category breakdown or a clear route to it, distinguishing wins from nominations.
3. **Medium - Keep the sticky alert from obscuring results.** While scrolling, the persistent access notice overlays the top edge of the poster rows. Reserve space for it or use a more compact treatment, and verify the shared shell on other long catalog pages.
4. **Medium - Improve navigation through the result set.** The first page reports 1,270 records across 22 pages and exposes only Previous/Next. Consider a page-jump or page-size control if it fits the shared catalog pagination pattern.
5. **Medium - Reuse the shared catalog card improvements.** The five-column grid compresses synopsis and award details; several cards repeat "Director: Not listed," and one uses a dark missing-poster fallback. Apply the shared card-width, metadata, unavailable-field, and poster-fallback guidance from Catalog.

## Administration Screens

Administration reviews are now kept in the [administration UI design improvement backlog](ui-design-improvements-administration.md), including System, Dashboard, Torrent settings, Recommendation settings, and OMDb Provider.

## Next Screens

Add the next catalog, recommendation, award, or personal-library screen here after its visual review. Continue administration reviews in the [administration backlog](ui-design-improvements-administration.md).