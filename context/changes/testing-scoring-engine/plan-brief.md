# Scoring Engine Truth Table — Plan Brief

> Full plan: `context/changes/testing-scoring-engine/plan.md`
> Research: `context/changes/testing-scoring-engine/research.md`

## What & Why

Test-plan §3 Phase 1. Two of the product's highest-ranked risks are currently protected by
nothing but manual attestation: that a member's points match **their league's** configured
rules (risk #1), and that two leagues on one tournament don't silently converge on identical
totals (risk #2). The second one is the product's wedge — per-league custom scoring is the
whole pitch — and `scoring-engine-standings` criteria 2.3 and 5.8 were checked by hand only.

## Starting Point

The server has **no test infrastructure whatsoever**: `prediction-league.slnx` lists five
projects, none of them a test project, and `deploy-backend.yml` has no `dotnet test` step.
The code itself is unusually ready — `PredictionScorer.Score` and `MatchOutcome.FromMatch`
are pure statics with no clock, no I/O and no state, and `MatchScoringService` takes all
five of its dependencies through the constructor. The reason there are no unit tests is not
that the code resists them.

## Desired End State

`dotnet test src/server/prediction-league.slnx` runs a green suite that goes red if any
scoring parameter starts or stops awarding, if rules stop stacking, if a `MissedPenalty`
starts counting as a goal, if `MatchEvent.Id` enters the first-scorer ordering, or if two
leagues stop diverging for an identical forecast. CI runs that suite, so a red test blocks
the deploy. `test-plan.md` §6.1 carries the pattern for writing the next scoring test.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Test surfaces | Two: pure Domain, plus `MatchScoringService` with fakes | `Score` takes rules as an argument, so risk #2 is tautological there — it lives at `MatchScoringService.cs:89` | Research |
| Same-minute tie-break | Assert determinism + `MatchEvent.Id`-independence; never the winner | The last two ordering keys are surrogate keys with no football meaning; pinning them writes a mirror test | Plan |
| Defensive Domain guards | Test both (null-score throw, unmapped-type drop) | `MatchOutcome` is public Domain API — the guards are real contracts even if today's caller gates first | Plan |
| Truth-table breadth | Parameterised per parameter + named edge cases | Each test catches a distinct regression; avoids both the happy-path and redundant-copy anti-patterns | Plan |
| Project layout | One project, `PredictionLeague.Tests` | Five product projects and zero test projects — one package set is cheaper to keep in sync than two | Plan |
| Faking approach | NSubstitute | The four repository interfaces carry ~38 members; hand-rolled fakes would be mostly dead stubs that break on every new slice | Plan |
| Assertion library | Shouldly | MIT with no licence trap; FluentAssertions v8+ requires a paid commercial licence | Research |
| Runner | xUnit v3 on VSTest, **not** Microsoft.Testing.Platform | No `global.json`, so `dotnet test` defaults to VSTest; the MTP snippets in xUnit's docs assume a switch this repo lacks | Research |
| CI gate | `dotnet test` in the existing `build` job | Gate is live the moment tests exist; PR gating is Phase 5's job | Plan |
| Doc scope | Cookbook + test-plan status only | `AGENTS.md` correction deliberately left out — see Open Risks | Plan |

## Scope

**In scope:**
- `PredictionLeague.Tests` project, solution entry, package set
- `dotnet test` step in `deploy-backend.yml`'s build job
- Surface A — `PredictionScorer` truth table across all six parameters, plus stacking, blank forecasts, own-goal-as-pair, null guards
- Surface A — `MatchOutcome` derivation: `MissedPenalty` exclusion, card counting, first-scorer determinism and Id-independence, defensive contracts
- Surface B — `MatchScoringService` per-league isolation, zero-rule fallback, un-scoring, early exits
- `test-plan.md` §3 / §4 / §5 / §6.1 updates

**Out of scope:**
- Engine tolerances the product forbids: zero points, negative points, duplicate parameters (blocked by `LeaguesController.cs:378` and the unique index)
- Whether `NormalGoal` should outrank `OwnGoal` in the same minute — a possible product defect, not a test-design question
- The stale `AGENTS.md` scoring-parameter count
- Standings (risk #7 → Phase 3), the write-path defect (risk #3 → Phase 2)
- Any database, `WebApplicationFactory`, or PR-triggered workflow

## Architecture / Approach

```
Surface A (pure, no fakes)          Surface B (NSubstitute)
┌──────────────────────────┐        ┌────────────────────────────────┐
│ PredictionScorer.Score   │        │ MatchScoringService            │
│ MatchOutcome.FromMatch   │        │   ↑ 4 repos + ILogger faked    │
│                          │        │   → captures the dictionary    │
│ risk #1                  │        │     handed to SetAwardedPoints │
└──────────────────────────┘        │ risk #2 — guards line 89       │
                                    └────────────────────────────────┘
```

The oracle discipline is the substance of this phase: every expected total is derived from
the league's rule configuration plus football semantics, never from running the engine and
recording what came back. Where research graded an oracle source **Tier B** — the document
restating the implementation's shape rather than recording an independent decision — the
test asserts the behaviour instead of the formula.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Test project and CI gate | `PredictionLeague.Tests` + solution entry + `dotnet test` in CI | Copying xUnit's MTP setup snippets, which need a `global.json` this repo doesn't have |
| 2. `PredictionScorer` truth table | Six parameters × award/no-award, stacking, blank forecasts, own goals | The oracle problem — a mirror test that computes the expected value the way the engine does |
| 3. `MatchOutcome` derivation | `MissedPenalty` exclusion, cards, first-scorer determinism, guards | Over-asserting the tie-break and pinning surrogate keys as if they were football rules |
| 4. `MatchScoringService` isolation | Two leagues diverge; zero-rule scores 0; unfinished un-scores | Asserting only "the totals differ", which coincidence satisfies as well as real isolation |
| 5. Cookbook and rollout sync | `test-plan.md` §3/§4/§5/§6.1 | Leaving §6.1 too vague to write the next test from |

**Prerequisites:** .NET 10 SDK (10.0.400 confirmed installed); network access for the first
NuGet restore. No database, no Azure access, no running app.

**Estimated effort:** ~2 sessions across 5 phases. Phase 1 is mostly mechanical; phases 2–4
are the real work; phase 5 is short.

## Open Risks & Assumptions

- **The CI gate covers pushes to `main`, not PRs.** `deploy-backend.yml` has no `pull_request`
  trigger, so between this phase and Phase 5 a regression is caught at merge time rather than
  at review time. Recorded deliberately, not overlooked.
- **`AGENTS.md` stays wrong.** It documents four scoring parameters where the enum has six,
  and still claims no test suite exists. Left out of scope by decision — worth a separate
  one-line change, because the next agent reading it will build a truth table missing a third
  of the surface.
- **The same-minute ordering may be a real defect.** `MatchEventTypeId` ordering means a
  `NormalGoal` always beats an `OwnGoal` or `Penalty` in the same minute regardless of true
  order, and admin entry is minute-granular so this is reachable. The tests deliberately do
  not ratify it; the product question stays open.
- **Own-goal `TeamId` correctness is enforced outside the Domain.** These tests prove the
  pair rule holds; they cannot prove admin entry populates `TeamId` correctly.
- **Package versions were confirmed on nuget.org this session** but are resolved with
  `dotnet add package` at implementation time. A different xunit.v3 major would change the
  runner contract and should stop the phase rather than be absorbed.

## Success Criteria (Summary)

- A member's awarded total can be traced to their own league's rules by reading a test, and
  the suite fails if that stops being true.
- Two leagues on one tournament provably score one identical forecast differently — proven
  by a mutation check against `MatchScoringService.cs:89`, not just a green assertion.
- The next person writing a scoring test reads `test-plan.md` §6.1 and does not need this
  plan.
