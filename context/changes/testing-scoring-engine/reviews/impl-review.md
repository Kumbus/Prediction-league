<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Scoring Engine Truth Table

- **Plan**: `context/changes/testing-scoring-engine/plan.md`
- **Scope**: Full plan — Phases 1–5 of 5
- **Date**: 2026-08-31
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 2 observations
- **Triage**: 3 fixed (F1, F2, F4), 1 skipped (F3) — completed 2026-08-31

## Scope detected

Commits `7e9ed33..02c0d60` (6 commits). 13 files changed, 2735 insertions, **0 deletions from production source** — no file under `PredictionLeague.Domain/`, `Application/`, `Api/`, `Infrastructure/` or `Functions/` was modified. For a test-only change this is the right shape, and it was verified after the review's own mutation runs: `git status --porcelain src/server/` is clean.

| Planned | In diff | Verdict |
|---|---|---|
| `PredictionLeague.Tests.csproj` | ✅ | MATCH (package set adapted — see Deviations) |
| `prediction-league.slnx` | ✅ | MATCH |
| `Domain/Scoring/PredictionScorerTests.cs` | ✅ | MATCH |
| `Domain/Scoring/ScoringFixtures.cs` | ✅ | MATCH |
| `Domain/Scoring/MatchOutcomeTests.cs` | ✅ | MATCH |
| `Infrastructure/Scoring/MatchScoringServiceTests.cs` | ✅ | MATCH |
| `.github/workflows/deploy-backend.yml` | ✅ | MATCH |
| `context/foundation/test-plan.md` §3/§4/§5/§6.1 | ✅ | MATCH (§4 wording inverted — see Deviations) |
| `change.md` | ✅ | MATCH |
| — | `global.json` | EXTRA — required by the runner deviation, approved |
| — | `test-plan.md` §6.6 | EXTRA — sanctioned by that section's own instruction |

## Deviations (surfaced and approved in-flight)

Both were raised via `AskUserQuestion` before implementation continued, and both are recorded in `change.md` and `test-plan.md` §4/§6.6. Neither is a silent drift, which is why Plan Adherence is PASS rather than WARNING.

1. **Runner.** The plan's *Critical Implementation Details* said `dotnet test` defaults to VSTest with no `global.json`, and to avoid MTP. False on this toolchain: xunit.v3 4.0.0 (the version the plan itself pins) embeds Microsoft.Testing.Platform, and the .NET 10 SDK removed the VSTest target — `dotnet test` errored with *"Testing with VSTest target is no longer supported… on .NET 10 SDK and later."* Resolved by a repo-root `global.json` selecting MTP, and by dropping `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` (both VSTest-path packages). All other resolved versions match Key Discoveries exactly.
2. **§4 wording.** The plan's Phase 5 contract asked §4 to "note the VSTest-not-MTP decision and its `global.json` reason." Writing that would have recorded a falsehood, so §4 documents the actual runner setup instead.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

### Success Criteria — all 31 re-verified this session

| Check | Result |
|---|---|
| `dotnet restore` / `dotnet build` (solution) | Restored; Build succeeded, 0 errors |
| `dotnet test src/server/prediction-league.slnx` | **83 passed, 0 failed, 0 skipped** |
| 2.2 — six `ScoringParameter` members in the test file | All six present |
| 3.3 — append `.ThenBy(x => x.Event.Id)` | **Red (1 failure)**, reverted |
| 4.3 — pin rule selection to one league | **Red (2 failures)**, reverted |
| 5.1 — `TBD` in §6.1 | 0 matches |
| 5.2 — §3 Phase 1 Status | `complete` |
| 5.3 — §4 `unit (server)` row | no longer `none yet` |
| Progress rows | 31/31 `[x]`, 0 SHA-less |

Manual rows are not rubber-stamped: 3.4 (no tie-winner assertion), 3.5 (fixture ids match the seed), 3.6 (`MissedPenalty` `Code` exclusion), 4.4/4.5/4.6 all have observable evidence in the diff, and 3.6 was additionally proven by mutation during implementation.

### Scope Discipline — "What We're NOT Doing" all held

No test configures zero, negative or duplicate rule values (the only multi-parameter rule set is `ExactScore 5 + CorrectOutcome 2`). No test asserts a same-minute tie winner where only surrogate keys differ — the two winner-asserting ordering tests turn on `Minute` and on `MinuteExtra` (90′ before 90+1′), both football-meaningful. No `WebApplicationFactory`, `DbContext`, SQLite, Testcontainers or standings reference appears in test source. `AGENTS.md` untouched *by the change itself* — it was later edited during this review's triage (F1), a decision taken knowingly and after the fact, not a scope breach by the implementation. No PR trigger added to the workflow.

## Findings

### F1 — AGENTS.md now tells the next agent that no tests exist

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `AGENTS.md:28`, `src/server/AGENTS.md:11`
- **Detail**: Both onboarding files still say there is no test suite — root: *"No test suite exists yet in either unit. Don't claim tests pass — there are none to run."*; server: *"No tests exist yet. Don't claim any pass."* As of `02c0d60` there are 83 tests and a CI gate. These files are the first thing an agent reads, and they now instruct it to deny the suite exists. The same two files also list only four `ScoringParameter` members (`AGENTS.md:42`, `src/server/AGENTS.md:27`) against the enum's six — research flagged this as stale before the change began. The plan deliberately excluded AGENTS.md from scope, so this is residue of a correct scope decision, not a violation of it — but the residue is now actively wrong rather than merely incomplete.
- **Fix**: Open a small follow-up change correcting both files: replace the no-tests lines with the run command and project location, and extend the `ScoringParameter` list to all six. Keep it out of this change, whose scope decision stands.
- **Decision**: FIXED — user chose to fix in-session rather than defer. Both no-tests lines replaced with the `dotnet test` command, the suite's location, and the repo-root caveat (the root `global.json` selects the runner); both `ScoringParameter` lists extended to all six members.

### F2 — CI runs a full Debug solution build before the Release publish

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `.github/workflows/deploy-backend.yml:58`
- **Detail**: `dotnet test src/server/prediction-league.slnx --no-restore` carries no `-c`, so it builds all six projects in **Debug**. The two steps that follow build the same tree again in **Release** (`Publish API`, `Publish Functions`). The `build` job has `timeout-minutes: 10` and the Functions project carries a nested `WorkerExtensions` sub-build, so this is close to doubling the job's compile work. It also means the gate never exercises the configuration that actually ships — Release enables optimisations and disables `Debug.Assert`, so a Release-only failure would pass this gate.
- **Fix**: Add `-c Release` to the test step: `dotnet test src/server/prediction-league.slnx -c Release --no-restore`. The subsequent `dotnet publish -c Release` then reuses the same intermediate outputs rather than rebuilding, so the step is close to free, and the gate tests what deploys.
  - Strength: Removes the duplicate compile and closes the Debug/Release gap in one flag; no test code changes.
  - Tradeoff: Release builds are marginally slower per-project than Debug, and a first run pays for the Release build that publish would otherwise have paid for — net still a saving.
  - Confidence: MEDIUM — the reuse is standard MSBuild behaviour, but it has not been observed on this workflow (no PR trigger, so the change would only be seen on the next push to `main`).
  - Blind spot: The workflow has not run since the gate was added, so neither the current timing nor the improved timing is measured. `actions/setup-dotnet@v5` behaviour alongside the new root `global.json` is also unobserved in CI — the file has no `sdk` key and the step passes `dotnet-version: 10.0.x` explicitly, so it should be inert, but that is reasoning, not evidence.
- **Decision**: FIXED — test step is now `dotnet test src/server/prediction-league.slnx -c Release --no-restore`, with a comment recording why. Half the blind spot is closed: the suite was run locally under Release and is green, 83/83. CI timing remains unmeasured until the next push to `main`.

### F3 — Shared fixture lives under one test area's folder

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/server/PredictionLeague.Tests/Domain/Scoring/ScoringFixtures.cs`; consumed at `Infrastructure/Scoring/MatchScoringServiceTests.cs:8`
- **Detail**: `ScoringFixtures` is now shared by both test areas — the Infrastructure service tests reach across with `using static PredictionLeague.Tests.Domain.Scoring.ScoringFixtures;`. The plan placed it in `Domain/Scoring/` when only Domain tests existed; Phase 4 then added `LeagueWith` and `MatchInStatus` to it, which are service-test concerns living in a Domain-test folder. Harmless today at one cross-reference, but the folder split stops meaning what it says as more areas share the file.
- **Fix**: If a third consumer appears, move it to a top-level `Fixtures/` (namespace `PredictionLeague.Tests.Fixtures`). Not worth the churn for one cross-reference.
- **Decision**: SKIPPED — deliberately deferred to the third consumer, per the fix's own recommendation. Revisit when a test area outside `Domain/Scoring` and `Infrastructure/Scoring` needs these builders.

### F4 — Filler assertion in the Id-independence theory

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/server/PredictionLeague.Tests/Domain/Scoring/MatchOutcomeTests.cs:178`
- **Detail**: `scenario.ShouldNotBeEmpty();` asserts a property of the test's own label, not of the system under test. The `scenario` parameter exists to make `TheoryData` cases readable in the runner output, which xUnit does from the parameter itself — the assertion is not needed for that and can never fail. It reads as a real assertion at a glance, which is the mild cost.
- **Fix**: Delete the line. The parameter still names each case in the test output without it.
- **Decision**: FIXED — line deleted. The claim was then verified rather than assumed: re-running the Id mutation showed both case labels still printed (`scenario: "two different scorers in the same minute"`, `scenario: "two goals in the same minute with no scorer record..."`), so xUnit's parameter formatting supplies the naming on its own. Mutation reverted; `PredictionLeague.Domain/` clean.

## Notes for the next reviewer

- Both mutation checks (3.3, 4.3) were re-run independently during this review, not taken on trust from the implementation log. Both turned red and were reverted; production source is clean.
- The Id-independence test's design is the one non-obvious thing in the change and is worth understanding before editing `MatchOutcome`'s ordering: an **appended** `.ThenBy(Id)` is inert unless two events tie on all four existing keys, so the fixture deliberately includes two same-minute goals with no scorer recorded (`PlayerId == Guid.Empty`), which tie on every key and differ only in credited team. Remove that case and criterion 3.3 silently stops guarding anything.
