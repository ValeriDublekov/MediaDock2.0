# Workflow Efficiency Plan

## Goal

Reduce avoidable approval prompts, repeated test runs, broad log reads, and failed edit attempts while preserving repository safety rules and required verification. Do not claim exact token savings unless the active usage data actually provides them.

## Current Baseline

- `scripts/run_tests.py` already defaults artifacts to the workspace-local `.test-tmp/`, supports `--artifacts-dir`, suppresses passing-test output, and summarizes recognized failures.
- Repository instructions already require the test runner, narrow filters, Docker-backed integration tests when relevant, and the AI documentation index before project edits.
- `TEST_DIAGNOSTICS_PLAN.md` already covers artifact placement, EF log noise, concise diagnostics, and the file inventory. This plan does not add another runner or log parser.
- The local session store does not expose exact token usage or a complete record of approval prompts. Use observable proxies and label uncertainty.

## Guardrails

- Never bypass an approval prompt or weaken production, security, or test requirements to save time.
- Do not contact live RSS or OMDb services during routine tests; use the existing isolated fixtures.
- Preserve the repository rule: run tests only with `python -B scripts/run_tests.py <suite>`.
- Keep `.test-tmp/` ignored and inside the workspace. Inspect generated reports only when a failure needs diagnosis.

## Rollout

### Step 1: Encode the missing guidance

1. Update `TESTING.md` to state explicitly that the default artifacts are under `.test-tmp/`, while retaining the custom `--artifacts-dir` option and report-retention behavior.
2. Add a concise workflow section to `mediadock.instructions.md`: inspect exact local context, patch one file at a time for long documents, run the narrow check immediately after a substantive edit, and start failure diagnosis from the runner summary.
3. Keep the current runner implementation. Change it only if a measured session shows a real gap, such as artifacts still escaping the workspace or useful failure details being omitted.

### Step 2: Apply the workflow in the next feature session

1. Read the relevant plan, AI documentation index, and current `git status`; identify the smallest code or documentation surface that owns the behavior.
2. Before editing, state one local hypothesis and the cheapest check that could disconfirm it. Use exact nearby text for a small, single-file patch instead of a multi-document bulk patch.
3. After the first substantive edit, run its narrowest available check before further exploration or edits. Use `git diff --check` for documentation-only changes when there is no more specific executable check.
4. Select one focused suite/filter for each materially changed behavior. Check Docker once before relevant integration tests. Run `all` once at the end only when the task or release gate requires full verification.
5. If a focused test fails, inspect the runner summary first, then only the relevant excerpt from the reported `.test-tmp/` log or report. Fix the cause and rerun that same filter; do not reopen complete TRX/EF output unless the summary is insufficient.
6. Do not repeat a passing suite without a source change or a required final gate. When a fix changes code after the full suite, rerun the focused check and the required full gate.

### Step 3: Measure and decide

For the next completed feature session, record:

- Test commands and approximate elapsed time, separating focused runs from the final full suite.
- Reruns caused by actual failures versus redundant reruns with no intervening change.
- Full log/report reads and whether the concise runner summary was sufficient.
- Approval prompts observed, their cause, and whether a workspace-local path would have avoided them. Do not infer an exact count when the tool history does not provide one.
- Token usage only if a trustworthy usage source exposes it; otherwise record `unavailable` and use the proxies above.

After that session, compare the observations with this baseline. Make further runner or instruction changes only for a repeated, evidenced problem; do not add wrappers or automation speculatively.

## Success Criteria

- No approval prompt is caused solely by opening test artifacts outside the workspace.
- Passing logs are not opened; failing logs are inspected only as narrowly as needed.
- Each changed behavior gets a focused check, with reruns tied to a failure/fix or an explicitly required gate.
- Required Docker integration and full-suite gates are run, not silently skipped.
- The retrospective reports measured facts separately from estimates and clearly says when token or approval data is unavailable.
