# Workflow Efficiency Plan

## Objective

Make repository work predictable and low-overhead: inspect the smallest relevant context, make a focused change, and run the cheapest check that can falsify it. Never reduce efficiency by bypassing approvals, security controls, or required verification.

## Scope

- Put mandatory, task-level workflow rules in `.github/instructions/mediadock.instructions.md`.
- Keep this file as the rollout and evaluation plan, not a second copy of the instructions.
- Keep `TESTING.md` as the source for test commands and artifact behavior.
- Do not change the runner or add automation without evidence of a concrete gap.
- Do not claim exact token or approval savings when the available history cannot establish them.

## Current Baseline

- Phase 1 is implemented: the repository instruction file defines the ordered context-edit-check workflow and plan/progress file triggers.
- `TESTING.md` documents the runner's `.test-tmp/` default, unique per-run directories, `--artifacts-dir`, retained artifacts, and concise failure summaries.
- `scripts/run_tests.py` already provides these behaviors; no runner code or automation was needed.
- Approval prompts are not guaranteed to be fully recorded in session history. Report only prompts directly observed; never infer counts from file paths or test artifacts.

## Plan and Progress File Rules

Use separate planning files only when the work spans multiple sessions or has multiple independently verifiable phases. Keep short or single-phase work in one plan or task; do not create a progress file just to record routine commands.

- **Master plan:** goal, constraints, dependencies, phase links, and overall acceptance criteria.
- **Phase file:** create one only when a phase needs its own tasks, focused checks, or exit criteria; link it to the master.
- **Progress file:** create one only for multi-session work. Track the active phase, completed checks, blockers, and next action. Do not duplicate the plan or keep a second narrative log.

At the start of a planned multi-session task, inspect the worktree and read the master, active phase, progress tracker if present, and only the task-specific documentation. Read another phase file only when a dependency requires it. Keep one source of truth for each fact.

## Rollout

### Phase 1: Encode the workflow (complete)

1. Add an `Efficient Task Workflow` section to `.github/instructions/mediadock.instructions.md` with ordered rules for local context, pre-edit hypothesis, smallest change, immediate focused validation, failure triage, and stopping criteria.
2. Add the plan/progress file triggers above to the instruction file. Do not require a retrospective for short or routine tasks.
3. Correct `TESTING.md` to document `.test-tmp/`, `--artifacts-dir`, unique per-run directories, and retained reports.
4. Leave `scripts/run_tests.py` unchanged; its current behavior already provides the required artifact handling and concise failure summaries.

**Exit criteria met:** the instruction file tells the AI what to do, in what order, and when to stop; the test guide matches the runner; no runner code or extra automation was added without evidence.

### Phase 2: Apply and evaluate (pending)

1. Apply the instruction workflow in the next non-trivial feature task. This documentation review is not that feature-task evaluation. Use the plan/progress file structure only if its triggers are met.
2. At completion, check whether each changed behavior got a focused check, any passing suite was rerun without a source change or required gate, and any failure required opening more output than the runner summary plus a relevant excerpt.
3. Record only directly observed approval prompts. Do not estimate token use or claim an efficiency improvement from one task.
4. Change instructions to fix a clear contradiction or ambiguity immediately. Add runner automation only after the same avoidable overhead appears in at least two independent tasks, unless a concrete correctness or safety defect needs immediate repair.

**Evaluation rule:** one completed feature task checks whether the instructions are usable; it is not statistical proof of saved time, tokens, or approvals. Keep the workflow if it meets the exit criteria. Adjust it only in response to observed friction.

## Acceptance Criteria

- The instruction file defines a smallest-relevant-context workflow and requires a focused check immediately after the first substantive edit.
- Repository tests run only through `python -B scripts/run_tests.py <suite>`; use the narrowest relevant suite/filter and run required integration or release gates.
- Failure diagnosis starts with the runner summary and reads only the relevant artifact excerpt unless evidence shows that is insufficient.
- No passing suite is repeated without an intervening source change or an explicitly required gate.
- Multi-session planning files are created only when the defined triggers apply; short tasks stay lightweight.
- Retrospectives are concise and optional for routine tasks; any reported approval or resource observations are directly supported by available evidence.
