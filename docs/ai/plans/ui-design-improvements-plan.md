# UI Design Improvement Plan

## Goal and Boundaries

Improve safe operation, browsing efficiency, and interpretation of titles, ratings, scores, and awards while preserving the existing visual language, security notices, and data semantics.

This index defines the proposed sequence, not implementation approval. Do not mutate deployed state or retain personal data, credentials, raw logs, or authenticated screenshots during planning/review. Contract, security, and persistence changes require separate approval.

## Resume Work

Read this index, the small [progress tracker](ui-design-improvements-plan-progress.md), and only the active phase. Read another phase only for a dependency. Update current status, completed work/checks, blockers, and next action in the tracker, not this index or phase headings. Keep verified ownership paths and durable design decisions in the relevant phase.

## Phase Index

| Phase | Priority / initiatives | Dependencies |
| --- | --- | --- |
| [1. Evidence and shared shell](ui-design-improvements-plan-phase-1-shell.md) | P1 / UI-01: content visibility and context | Approved slice; verified local ownership |
| [2. Safe operations and diagnostics](ui-design-improvements-plan-phase-2-operations.md) | P1 / UI-02 through UI-05: intent, lifecycle, validation, diagnosis | Verified workflow contracts; Phase 1 for sticky layout only |
| [3. Browsing and personal libraries](ui-design-improvements-plan-phase-3-browsing.md) | P2 / UI-06 through UI-08: density, lookup, personal meaning | Phase 1 offsets; Phase 2 conventions for mutations |
| [4. Recommendations and awards](ui-design-improvements-plan-phase-4-domain-meaning.md) | P2 / UI-09 and UI-10: ranking explanation and categories | Phase 3 hierarchy; verified domain data |

P1 prioritizes operational/data risk and broad blockers; P2 builds on those foundations. Original High/Medium/Low evidence remains in each initiative. Phase 4 data checks can run alongside Phase 2; safety work need not wait for browsing refinements. Each phase needs separate approval and its own exit gate.

## Shared References and Acceptance

- Read the [common validation gate](ui-design-improvements-plan-validation.md) when planning or validating an implementation slice. Completion requires both that gate and the active initiative's measurable criteria, without silently changing contracts.
- Consult [scope, evidence, coverage, and open questions](ui-design-improvements-plan-scope.md) when deciding ownership, feasibility, or scope; it is not required reading for every resume.
- The [general review](ui-design-improvements.md) and [administration review](ui-design-improvements-administration.md) retain the original observational evidence. Read only the relevant screen sections as needed.

