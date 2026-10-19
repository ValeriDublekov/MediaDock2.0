# Phase 2: API Contracts

Master plan: [AI Documentation Optimization Review](DOCUMENTATION_OPTIMIZATION_REVIEW.md)

Status: Completed 2026-10-19.

## Goal

Make API contract reading domain-specific while preserving every externally observable route, request, response, validation, access, and error rule.

## Assessment

At the pre-split assessment snapshot, [API_CONTRACTS.md](API_CONTRACTS.md) was 168 lines / 40.2 KiB and had been touched by 45 commits across 10 distinct dates. It spanned several independently changing domains; these are baseline figures, not the current page size.

## Target Structure

- Keep `API_CONTRACTS.md` as a concise entry page for shared paging, error, and access conventions.
- Create `API_CATALOGS_AND_AWARDS.md` for catalog, Oscar, and Golden Globes contracts.
- Create `API_IDENTITY_AND_PERSONAL_DATA.md` for authentication, favorites, and personal-rating contracts.
- Create `API_OPERATIONS_AND_JOBS.md` for operations, ingestion, and background-job contracts.
- Keep each domain's DTOs, request/response rules, and query behavior beside its routes. Architecture and persistence explanations belong in their respective references.

## Tasks

1. Inventory every route and its query, request, response, validation, authorization, and error behavior. Treat `server/` source and tests as authoritative.
2. Assign each route and contract fact to exactly one domain page. Preserve security and validation edge cases, including behavior that differs between access modes.
3. Move domain detail into the three focused pages; retain only shared conventions and links in `API_CONTRACTS.md`.
4. Remove duplicated architecture and persistence narration only after confirming its canonical reference. Update the AI index so API tasks can route directly to the relevant domain page.

## Focused Checks

- Compare each documented route and edge case with its owning endpoint and relevant integration tests; confirm no route or observable rule was dropped or assigned twice.
- Check all incoming links to moved sections and all relative links in the edited pages. Run `git diff --check`.
- This phase changes documentation only; do not run application test suites unless source or test code is also changed.

## Exit Criteria

- `API_CONTRACTS.md` is a short, useful router for shared conventions.
- Each API route has one canonical domain home, with its DTO and observable behavior adjacent to it.
- The task index routes API readers to only the relevant contract page, and no architecture, persistence, security, or validation rule has been lost.
