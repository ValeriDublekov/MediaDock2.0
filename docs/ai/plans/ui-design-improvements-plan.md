# UI Design Improvement Plan

## Goal and Boundaries

First fix obstructed use and unsafe or misleading operations. Then improve finding and comparing titles; defer extra controls and visual polish. Preserve security notices, data semantics, and the existing visual language.

This index defines the proposed sequence, not implementation approval. Do not mutate deployed state or retain personal data, credentials, raw logs, or authenticated screenshots during planning/review. Contract, security, and persistence changes require separate approval.

## Resume Work

Read this index, the small [progress tracker](ui-design-improvements-plan-progress.md), and only the active phase. Read another phase only for a dependency. Update current status, completed work/checks, blockers, and next action in the tracker, not this index or phase headings. Keep verified ownership paths and durable design decisions in the relevant phase.

## Work Order

| Priority | What to address | Done when |
| --- | --- | --- |
| P1: now | Overlap or inaccessible controls; unclear consequential actions; unsafe submission/duplicate activation if confirmed; misleading failure/retry states | The selected problem is resolved and its focused safety/accessibility checks pass |
| P2: next | Title lookup, readable cards/compact tables, distinct rating/score labels, accurate scope, and access to existing explanations/award details | The selected everyday task works clearly without changing data/query semantics |
| P3: deferred | Additional filters/page controls, tokenized inputs, expanded analytics/explanations needing new data, and cosmetic consistency | Separate need and scope approval; not required to close P1/P2 work |

Choose the highest-priority unblocked problem, not an entire phase. Verify unobserved behavior locally before scheduling a fix; if already correct, record no change needed. Within P1, address active harm/blockage first, then operational/data risk and breadth of benefit. A blocker stays recorded but does not stop independent approved work. Do not start P3 while approved, unblocked P1/P2 work remains.

## Phase References

Phase numbers identify work packages, not mandatory completion order. Select and approve one small scope; apply its relevant criteria and the common gate. Stop when they pass, leaving unrelated candidates deferred. Existing implementation approval is not expanded by this reordering; original High/Medium/Low observations are unchanged.

| Read for the selected problem | Dependency |
| --- | --- |
| [1. Shell and context, UI-01](ui-design-improvements-plan-phase-1-shell.md) | Existing shell verification; density is P2, not a P1 exit condition |
| [2. Operations and diagnostics, UI-02 through UI-05](ui-design-improvements-plan-phase-2-operations.md) | Verify the selected operation; no whole-phase prerequisite |
| [3. Browsing and personal libraries, UI-06 through UI-08](ui-design-improvements-plan-phase-3-browsing.md) | Only the shell/action contracts actually touched |
| [4. Recommendations and awards, UI-09 and UI-10](ui-design-improvements-plan-phase-4-domain-meaning.md) | Existing score/category data; no full card redesign prerequisite |

## Shared References and Acceptance

- Read the [common validation gate](ui-design-improvements-plan-validation.md) when planning or validating a slice. Completion requires that gate and the selected criteria, not every candidate in its phase, without silently changing contracts.
- Consult [scope, evidence, coverage, and open questions](ui-design-improvements-plan-scope.md) when deciding ownership, feasibility, or scope; it is not required reading for every resume.
- The [general review](ui-design-improvements.md) and [administration review](ui-design-improvements-administration.md) retain the original observational evidence. Read only the relevant screen sections as needed.

