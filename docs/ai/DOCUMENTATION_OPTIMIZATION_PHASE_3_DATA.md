# Phase 3: Data Contracts

Master plan: [AI Documentation Optimization Review](DOCUMENTATION_OPTIMIZATION_REVIEW.md)

## Goal

Make persistence references independently readable by schema and behavior while preserving canonical ownership, migration hazards, and database-enforced invariants.

## Assessment

[DATA_CONTRACTS.md](DATA_CONTRACTS.md) is 144 lines / 40.6 KiB and was touched by 25 commits across 9 distinct dates in the review snapshot. It combines schema reference, ownership rules, and transaction/idempotency behavior.

## Target Structure

- Keep `DATA_CONTRACTS.md` as a concise entry page for canonical ownership and migration cautions.
- Create `DATA_SCHEMA.md` for entities, fields, keys, indexes, constraints, and other schema-enforced invariants.
- Create `DATA_BEHAVIOR.md` for transaction boundaries, idempotency, and persistence behavior that is not itself a schema constraint.
- Keep API behavior in the API contract pages and test-case inventories in `TESTING.md`.

## Tasks

1. Inventory data ownership, migrations, schema objects, and persistence behaviors. Treat migrations, `server/` source, and tests as authoritative.
2. Map each fact to the entry page, schema page, behavior page, API contract, or testing guide so that each fact has one canonical home.
3. Move table/field/key/constraint details to the schema page and transaction/idempotency rules to the behavior page. Keep ownership and migration hazards easy to find from the entry page.
4. Shorten service-by-service narration and remove repeated implementation or test descriptions only after checking the owning reference. Update the AI index to route persistence tasks to the relevant pages.

## Focused Checks

- Compare documented schema and ownership rules with migrations, repositories, and relevant persistence/integration tests. Explicitly verify every migration hazard and database constraint remains represented.
- Check all incoming links to moved sections and all relative links in the edited pages. Run `git diff --check`.
- This phase changes documentation only; do not run application test suites unless source or test code is also changed.

## Exit Criteria

- `DATA_CONTRACTS.md` is a short router that preserves canonical ownership and migration cautions.
- Schema constraints and persistence behaviors are separated, accurately described, and each has one canonical home.
- API behavior and test inventories are not duplicated in data references; persistence tasks route directly to the appropriate page.
