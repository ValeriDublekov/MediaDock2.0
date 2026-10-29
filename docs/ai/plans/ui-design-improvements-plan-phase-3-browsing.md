# Phase 3: Browsing and Personal Libraries

[Master plan and phase index](ui-design-improvements-plan.md)

## Entry and Tasks

Proposed, not started. Use Phase 1 shell/scroll rules and Phase 2 action conventions where lists mutate. Add verified renderers, query owners, API fields, direct consumers, and tests to this file before implementation.

1. Establish card/table hierarchy using Catalog as the first local slice; inspect actual shared consumers before applying it elsewhere.
2. Extend verified shared behavior to recommendation, award, and personal lists without erasing screen-specific meaning. Apply UI-07 result context and UI-08 personal semantics in separate coherent slices.
3. Validate small and large collections, long/missing data, multiple pages, and responsive table/card layouts. Do not force identical column counts or metadata onto every list.

## UI-06: Readable Cards and Efficient Tables

**Priority / observation / sources:** P2. [Catalog](ui-design-improvements.md#catalog) records narrow five-column cards and tall Table rows (High), plus metadata hierarchy, missing posters, actions, and header context (Medium). Movie/Series Recommendations, Favorites, Personal Ratings, Oscar, and Golden Globes repeat density/missing-value concerns; Ignored Titles only requests narrower-width validation (Low). The simpler ignored cards were scannable at the reviewed desktop width.

**Impact / affected areas:** Faster comparison and less scrolling across title lists. Candidate shared areas are card sizing, fallback media, metadata/disclosure, action accessibility, and table density; actual renderers and each screen's primary fields remain unverified. Administration tables are not assumed to share the title-table implementation; their domain refinements stay in UI-04/UI-05.

**Dependencies:** Phase 1 space/offset rules; UI-02 naming; UI-08/UI-09/UI-10 define personal, recommendation, and award priorities. Verify existing detail access before collapsing metadata. Missing series information is not permission to fetch or invent it.

**Confidence / questions:** High confidence in recorded truncation/row height; column count is only a candidate remedy. Four columns, minimum width, and collection-aware layout need comparison at multiple widths. No demonstrated need to change Ignored Titles independently.

**Acceptance:**

- At 1458 x 816 and at a narrower/wider desktop, titles and primary signals are legible without clipping or overlap. Compare responsive minimum-width candidates rather than mandate four columns; keep poster dimensions/aspect ratio stable for loaded and missing media.
- Catalog Table presents compact comparison rows without an always-expanded synopsis. Secondary metadata remains reachable through labeled details; compactness does not remove essential year/type/rating/award context or row actions.
- Cards prioritize title, year/type, applicable rating/score, and screen-specific status. Long synopses and technical details have predictable secondary treatment and an accessible path to full content.
- Missing directors/award links do not repeat verbose placeholder prose. A concise absence indicator distinguishes unavailable from zero awards; missing posters retain a legible title and intentional fallback, not an unexplained dark box.
- Favorite watch/download status is discoverable near primary information; removing a favorite and ignoring a title remain distinguishable. Actions have accessible names, visible focus/hover treatment and non-overlapping artwork placement.
- Small collections such as eight titles do not stretch cards arbitrarily, and long titles do not resize controls or grid tracks. Ignored Titles changes only if the shared treatment or narrow-width checks demonstrate a benefit.

## UI-07: Find Titles and Preserve Result Position

**Priority / observation / sources:** P2. [Personal Ratings](ui-design-improvements.md#personal-ratings) has no visible title search (High). Catalog, recommendation lists, Favorites, Golden Globes, and both [Parse Log entry points](ui-design-improvements-administration.md#parse-log) record count/order/pagination/context concerns (Medium); a page-jump control is only a Golden Globes candidate.

**Impact / affected areas:** Less repeated scrolling, easier lookup in large libraries, and clearer current result scope. Share visual conventions for result context where useful, not query logic by assumption.

**Dependencies:** Confirm search/filter/pagination contracts and current client/server query ownership. Phase 1 offset rules apply to any sticky context/header. Keep recommendation sorting separate from catalog sorting and personal rating ordering.

**Confidence / questions:** High confidence in absence of visible Personal Ratings search, not absence of backend support. Existing page-jump/page-size support, More filters contents, and users' demand for additional controls are unverified.

**Acceptance:**

- Personal Ratings offers an explicitly labeled title search with keyboard access, clear/reset behavior, loading/error/no-match feedback, and preserved source/sort context. Confirm title scope and paging behavior; new backend search is a separately approved contract slice if unsupported.
- Counts and page position are available beside list controls before long results. For recommendations, the active ranking order is explicit. Long scans have a compact context/return mechanism that does not cover content or focus; sticky is not mandatory.
- Relevant table column headings remain discoverable while scrolling. Pagination has consistent title/record wording within its domain, preserves active filters, and does not display useless Previous/Next controls for a single page.
- Golden Globes gets page jump/size only if need and support are established; otherwise retain truthful current/total page information without inventing controls. Extra personal year/rating filters are likewise deferred until justified.
- Synthetic fixtures cover zero, one page, several pages, changed filters, clear search, and a page becoming invalid after filtering; no duplicate/lost rows or unexpected ordering changes.

## UI-08: Honest Scope and Personal Rating Meaning

**Priority / observation / sources:** P2. [Favorites](ui-design-improvements.md#favorites) uses movie-only context for mixed results (High); [Golden Globes](ui-design-improvements.md#golden-globes-catalog) calls mixed results films (High). [Personal Ratings](ui-design-improvements.md#personal-ratings) needs clearer personal/public rating separation (High), provenance/dates and quieter IMDb IDs (Medium); [Import Ratings](ui-design-improvements.md#import-ratings) needs scannable instructions and a comfortable form measure (Medium/Low).

**Impact / affected areas:** Prevent users misunderstanding what a collection contains, whose rating they see, or what a date represents. Keep recommendation scores separately defined in UI-09. Import mutation safety remains UI-02/UI-03.

**Dependencies:** Confirm intended mixed movie/series scope, personal-rating source fields, date definitions, and exact export instructions. Neutral terminology does not require adding a type filter. Do not infer provenance solely from missing dates or values.

**Confidence / questions:** High confidence in wording and competing rating signals. Imported versus MediaDock-entered provenance and vote/import/record dates may not all be available separately.

**Acceptance:**

- Favorites breadcrumb/helper text and Golden Globes search/count labels accurately describe verified content using consistent neutral terms where mixed. Routes and collection membership do not change merely to fit copy.
- Every personal/public rating has an explicit accessible label; Your rating has a consistent primary position distinct from IMDb rating and recommendation score. Check equal values, missing IMDb ratings, and long titles without relying on color.
- The MediaDock-only filter explains its actual inclusion rule. Dates name only the event the data supports: vote date, imported date, or recorded date; absent provenance is stated as unknown, not invented.
- IMDb identifiers remain available for identification/copying as secondary detail, without becoming the primary browse label. Favorites statuses and ignored-list semantics remain compatible with UI-02/UI-06.
- Import instructions present verifiable export steps, a readable/copyable exact archive path, and a structured CSV example within a restrained responsive form width; format/size/merge information remains beside the action.

## Validation and Exit

Use synthetic mixed movie/series collections, unavailable metadata/posters, long titles, equal and missing rating values, imported/local/unknown provenance, and multi-page libraries. Apply the common responsive/accessibility gate and focused tests per verified component/query owner; preserve all existing mutation and query semantics.

Exit when finding a known fixture title, comparing primary signals, reaching details/actions by keyboard, and retaining filter/page context pass without layout overlap. Document unsupported provenance/search data separately rather than presenting a misleading UI.