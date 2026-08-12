<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Custom Scoring Rules (S-04)

- **Plan**: `context/changes/custom-scoring-rules/plan.md`
- **Scope**: Phases 1–2 of 2 (Phase 1 committed `f1f7d61`; Phase 2 code complete, manual criteria pending)
- **Date**: 2026-08-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 1 observation — both triaged and fixed

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS (8 manual pending) |

## Grounding

- **File coverage**: 10 planned files, 10 changed, 0 missing. One additional file, `src/client/src/components/leagues/ScoringCard.tsx`, is the extraction Phase 2 §4 explicitly conditioned on ("extract the edit body if `LeagueDetailPage` starts carrying two full render trees inline") — planned-by-condition, not scope creep.
- **Automated criteria, all 5 re-run and passing**: `dotnet build` (0 errors) · `dotnet ef migrations has-pending-model-changes` ("No changes have been made to the model since the last migration") · no `Microsoft.EntityFrameworkCore` import in `LeaguesController.cs` (0 matches) · `npm run build` (tsc + vite clean) · `npm run lint` (clean).
- **Guardrails**: all seven "What We're NOT Doing" items hold — no scoring engine, no tests, no schema/enum change, no data migration, no join/rename/delete, no rule history, no organizer transfer.
- **Progress**: 10/18 at review time. Phase 1 8/8; Phase 2 automated 2/2, manual 2.3–2.10 pending.

## Findings

### F1 — ReplaceScoringRulesAsync silently half-writes if handed an untracked League

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs:48-80`
- **Detail**: Correctness rests entirely on `league` being tracked, which only `GetForUpdateAsync` guarantees. `GetWithDetailAsync` sits three lines above it, returns the identical graph, and is `AsNoTracking`. Handed a detached league: `Context.Set<ScoringRule>().Remove(existing)` still deletes (Remove attaches a detached entity as `Deleted`), but `existing.Points = points` and `league.ScoringRules.Add(...)` are both lost — `SaveChangesAsync` returns clean on a half-written config and the controller replies 200 from the in-memory graph, so the client shows a save that never happened. Latent, not live: the only caller (`LeaguesController.cs:208`) uses `GetForUpdateAsync` correctly. Related to the `lessons.md` boundary-debt rule — the interface documents the requirement but nothing enforced it.
- **Fix**: Throw when the league isn't tracked, mirroring the detached-state check already in `CreateAsync` (`:47`).
  - Strength: Three lines, the file's own idiom, no behaviour change for the correct caller.
  - Tradeoff: None meaningful — unreachable on the current call path.
  - Confidence: HIGH — `Context.Entry(...).State` already used this way one method away.
  - Blind spot: None significant.
- **Decision**: FIXED — guard added at the top of `ReplaceScoringRulesAsync`; `dotnet build` clean.

### F2 — Clearing a points field snaps the input back to 0

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/client/src/components/leagues/ScoringRulesFieldset.tsx:74-79`
- **Detail**: `Number.isNaN(e.target.valueAsNumber) ? 0 : e.target.valueAsNumber` maps an emptied field to `0`, and the controlled input immediately re-renders showing `0` — an organizer clearing "3" to type "12" sees a 0 appear mid-edit. Correctness unaffected: `min={1}` fails native validation, which is what makes criterion 2.6 hold. Mirrors the pre-existing `Number(e.target.value)` in the S-03 create form, so it matched repo precedent rather than regressing it.
- **Fix**: Render the field empty when points is 0 (`value={(active.get(d.parameter) ?? d.points) || ""}`), since 0 is not a legal value and therefore stands in for "cleared".
  - Strength: One-line change, validation behaviour identical.
  - Tradeoff: 0 becomes unrepresentable in the input — correct, since 0 is not legal.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED — `npm run build` and `npm run lint` clean.

## Notes (not findings)

- **Benign ladder deviation**: Phase 1 §4 says every caller computes the lock via `AnyKickedOffAsync`. `UpdateScoringRules` computes it once for the 409 gate and reuses that value for the response (`LeaguesController.cs:221`) rather than re-querying — same value, one fewer round-trip, still via `AnyKickedOffAsync`.
- **Manual criteria 1.4–1.8** are checked on the human's verbal confirmation, which is the flow `/10x-implement` prescribes; there is no diff evidence to cross-check them against and none is expected. 2.3–2.10 remain honestly unchecked at review time.
