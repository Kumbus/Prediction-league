---
date: 2026-08-31T11:29:39+02:00
researcher: Kumbus
git_commit: 9e65eb045e35bccf6e4b4edb4c0461c1719beb5c
branch: main
repository: Kumbus/Prediction-league
topic: "Test-plan Phase 1 — scoring engine truth table (risks #1, #2)"
tags: [research, codebase, scoring, prediction-scorer, match-outcome, match-scoring-service, standings, unit-tests, oracle]
status: complete
last_updated: 2026-08-31
last_updated_by: Kumbus
---

# Research: Test-plan Phase 1 — scoring engine truth table

**Date**: 2026-08-31T11:29:39+02:00
**Researcher**: Kumbus
**Git Commit**: `9e65eb045e35bccf6e4b4edb4c0461c1719beb5c`
**Branch**: `main`
**Repository**: Kumbus/Prediction-league
**Permalink base**: `https://github.com/Kumbus/Prediction-league/blob/9e65eb045e35bccf6e4b4edb4c0461c1719beb5c/`

## Research Question

`context/foundation/test-plan.md` §3 Phase 1 — "Scoring engine truth table":
prove that awarded points match a league's own rules (risk #1) and that two
leagues on one tournament do not converge to identical points (risk #2).

Research must produce the **oracle** — what the engine *should* do, sourced from
documents rather than from the implementation — plus the test surface, and (per
scope decision this session) the .NET 10 unit-test stack, since `test-plan.md` §4
records the server unit stack as "none yet — see §3 Phase 1".

Scope decisions taken at the start of this session:

- **Include the runner stack** in research, so the plan has no open stack question.
- **Mine the archive for oracle, then flag the rest** — prior change documents are
  treated as oracle where they record a decision; everything unresolved is listed
  as an Open Question rather than answered from the implementation.

## Summary

**The oracle exists and is unusually complete.** Nine of the eleven semantic
questions this phase needs answered are resolved in writing, mostly in
`context/changes/scoring-engine-standings/plan.md`. That is the good news and the
trap: that document is a *design decision record written alongside the
implementation*, not an independent specification like the PRD. Some of its lines
record a decision with a rationale (genuine oracle); others merely restate the
code's shape (weak oracle — cannot refute a bug in that shape). The Oracle Ledger
below grades every line so the plan does not silently promote a mirror into a truth.

**Five findings that change how Phase 1 should be planned:**

1. **Six scoring parameters, not four.** `AGENTS.md` documents four
   (`ExactScore`, `CorrectOutcome`, `CorrectGoalScorer`, `CorrectCardCount`);
   the enum has six — `CorrectYellowCards` and `CorrectRedCards` also exist
   (`Enums.cs:26-34`). A truth table built from `AGENTS.md` would silently miss a
   third of the surface. **`AGENTS.md` is stale and should be corrected.**

2. **Risk #2 is not provable at `PredictionScorer` alone.** `Score` takes the rule
   set as an argument, so passing two different rule sets and getting two different
   totals is close to tautological. The failure risk #2 names — two leagues
   converging — lives one layer up, in the per-league lookup at
   `MatchScoringService.cs:89` and the dictionary built at `:114-115`. Phase 1
   therefore needs **two unit surfaces**, both still DB-free: the pure scorer, and
   `MatchScoringService` driven through faked repositories (all four dependencies
   are constructor-injected interfaces, `MatchScoringService.cs:36-48`).

3. **Three of the engine's tolerances are unreachable in production** and must not
   be tested as if they were behaviour: zero points, negative points, and duplicate
   parameters in one rule set. `LeaguesController.cs:21-22,378` rejects points
   outside `1..1000`, and `ScoringRuleConfiguration.cs:16` puts a unique index on
   `(LeagueId, Parameter)`. Asserting a negative total, or double-counted points,
   would encode a state the product forbids.

4. **The first-scorer tie-break is arbitrary by construction and should not be
   pinned.** Ordering is `(Minute, MinuteExtra ?? 0, MatchEventTypeId, PlayerId)`
   (`MatchOutcome.cs:59-64`). The first two keys are meaningful; the last two are
   surrogate keys with no football meaning. A test that asserts *which* player wins
   a same-minute tie pins an implementation detail. What the sources actually
   justify asserting is **determinism** — the same events must always produce the
   same first scorer, and `MatchEvent.Id` must never participate, because a
   replace-all re-save mints fresh Guids and would move points between members
   (`MatchOutcome.cs:52-54`).

5. **A real product question falls out of that tie-break.** `MatchEventTypeId`
   ordering means a `NormalGoal` (Id=1) always beats an `OwnGoal` (Id=2) or
   `Penalty` (Id=3) scored in the same minute, regardless of true order. Admin entry
   is minute-granular, so same-minute pairs are plausible, not theoretical. This is
   an Open Question for the user, not something a test should ratify.

**Stack**: xUnit v3 on VSTest (the .NET 10 default — this repo has no `global.json`,
so no Microsoft.Testing.Platform opt-in), Shouldly for assertions (FluentAssertions
v8+ requires a paid commercial licence). No central package management exists, so
versions go inline. **No `dotnet test` step exists in CI today.**

## Detailed Findings

### 1. The engine — `PredictionLeague.Domain`

**Entry point.** `PredictionScorer.Score(Prediction, MatchOutcome, IEnumerable<ScoringRule>) : int`
(`PredictionScorer.cs:15-18`). Null-guards all three arguments (`:20-22`), then sums
`rule.Points` for every rule whose predicate holds (`:24-29`). Returns non-nullable
`int`; the "scored 0" vs "not scored" distinction is made by the caller, which stores
`int?` (`MatchScoringService.cs:93`).

**Purity.** No clock, no randomness, no statics, no I/O — asserted in the file header
(`PredictionScorer.cs:5-6`) and confirmed by reading. `MatchOutcome.FromMatch` is
likewise pure over its two arguments (`MatchOutcome.cs:30-77`). **Both are ideal unit
targets: no DB, no HTTP, no time.**

**The six predicates** (`PredictionScorer.cs:41-66`):

| Parameter | Predicate | Line |
|---|---|---|
| `ExactScore` | `PredictedHomeScore == HomeScore && PredictedAwayScore == AwayScore` | `:41-43` |
| `CorrectOutcome` | `Math.Sign(predH - predA) == Math.Sign(actH - actA)` | `:46-48` |
| `CorrectGoalScorer` | both predicted ids non-null **and** player id **and** team id both equal the outcome's | `:54-58` |
| `CorrectCardCount` | `PredictedTotalCards == outcome.TotalCards` | `:62` |
| `CorrectYellowCards` | `PredictedYellowCards == outcome.YellowCards` | `:63` |
| `CorrectRedCards` | `PredictedRedCards == outcome.RedCards` | `:64` |
| *(unknown)* | `_ => false` — awards nothing rather than throwing | `:66`, rationale `:34-36` |

Card comparisons are `int?` predicted against `int` actual — lifted equality, so a
blank prediction is `false`, never an exception. Comparisons are exact equality, not
a band or `>=`.

**Outcome derivation** (`MatchOutcome.cs`):

- Goal events = `Category == Goal` **minus** `Code == "MissedPenalty"` (`:57-58`).
  The exclusion is needed because `MissedPenalty` is seeded with `Category = Goal`
  (`MatchEventTypeConfiguration.cs:24`) — noted in the code as a known wart
  (`MatchOutcome.cs:19-20`). `OwnGoal`, `Penalty`, `NormalGoal` all count.
- First scorer = first of `.OrderBy(Minute).ThenBy(MinuteExtra ?? 0).ThenBy(MatchEventTypeId).ThenBy(PlayerId)`
  (`:59-64`), sorted **in memory** deliberately, to avoid SQL Server `uniqueidentifier`
  collation disagreeing with `Guid.CompareTo` (`:54-55`).
- `MatchEvent.Id` is deliberately excluded from the ordering: a replace-all save mints
  fresh Guids, which would move `CorrectGoalScorer` points between members on a no-op
  re-save (`:52-54`). **This is a rationale-backed decision — a strong oracle line.**
- Cards: `TotalCards` = every `Category == Card` event; yellows and reds split by
  `Code` (`:73-75`). Match totals, not per-team. Only `YellowCard` and `RedCard` are
  seeded (`MatchEventTypeConfiguration.cs:25-26`) — no second-yellow type exists.
- Events whose `MatchEventTypeId` is absent from the dictionary are **silently dropped**
  (`:44-48`), no log, no error.
- `FromMatch` **throws** `ArgumentException` when `HomeScore`/`AwayScore` is null
  (`:37-40`) — but that path is unreachable from the only production caller, which
  gates on `Status == Finished` first (`MatchScoringService.cs:67-79`).

**Own goals carry no special case.** The forecast is a *pair* — player id **and**
credited team id — so "player X credited to team B" expresses an own goal naturally
(`PredictionScorer.cs:50-52`). The Domain layer treats `MatchEvent.TeamId` as
authoritative and never inverts it; whichever layer records the event decides which
team an own goal is credited to.

### 2. The path — trigger to standings

Call chain:

1. Three write paths converge on the same service: `MatchesController.ReplaceEvents`
   (`MatchesController.cs:87-130`, saves events at `:120-121` then triggers scoring at
   `:124`), `MatchesController.Rescore` (`:164-174`), `TournamentsController.cs:260,299`,
   and the ingest path `FixtureIngestService.cs:165`.
2. `ScoringTrigger.TryScoreAsync` (`ScoringTrigger.cs:20-40`) swallows every
   non-cancellation exception and returns an admin warning string rather than failing
   the request (`:31-39`).
3. `MatchScoringService.ScoreMatchAsync` (`MatchScoringService.cs:50-104`) loads the
   match with events (tracked — `MatchRepository.cs:20-23` has no `AsNoTracking`,
   unlike the other reads), loads **every** prediction for the match across all leagues
   (`PredictionRepository.cs:103-109`, filtered on `MatchId` only), and either nulls all
   points (not finished, `:67-78`) or computes one shared `MatchOutcome` (`:81`) and
   scores each prediction against its own league's rules (`:87-94`).
4. Points land on `Prediction.AwardedPoints` via `SetAwardedPointsAsync`
   (`PredictionRepository.cs:115-140`) — one `SaveChangesAsync` for the whole match.

**Where a league's rules come from.** `LeagueRepository.ListByTournamentWithRulesAsync`
(`LeagueRepository.cs:43-49`) — `AsNoTracking().Include(l => l.ScoringRules)` filtered by
**TournamentId**, not LeagueId. Per-league selection then happens in memory at
`MatchScoringService.cs:89` via `rulesByLeague.GetValueOrDefault(prediction.LeagueId, NoRules)`,
against a dictionary keyed by league id built at `:114-115`.

**Caching.** No `IMemoryCache`, no statics, no singletons. `MatchScoringService` memoises
per scope only (`_rulesByTournament`, `_eventTypesById` — `:33-34`, `:109-119`), and every
repository plus the service itself is registered `Scoped`
(`DependencyInjection.cs:41,43,46,49,50`). Nothing outlives a request, so cross-request
staleness is not a live risk.

**The risk #2 divergence point is exactly one line** — `MatchScoringService.cs:89`.
Everything else is shared: one `MatchOutcome` for the whole match, one pure `Score`
function. Two leagues produce identical totals only when their rule sets are configured
identically *and* the member's forecasts match — coincidence, not shared state.

**Zero-rule fallback.** A league with no rules gets `NoRules` — a static empty list
(`MatchScoringService.cs:18`) — so every prediction scores 0, documented at `:84`. Not an
error, not a skip.

**Standings.** `PredictionRepository.ListStandingsAsync` (`:76-101`) re-derives each total
from stored `AwardedPoints` with a SQL-side `Sum(...) ?? 0` (`:88`), then sorts in memory
after materialising (`:97-100`, rationale `:94-96`). Ranks are assigned separately in
`StandingsController.Rank` (`StandingsController.cs:69-89`): shared rank on ties, next
distinct total skips (1, 2, 2, 4), computed server-side so every surface agrees
(`:66-68`). Members with no predictions appear with 0; members who left do not appear at
all (no `LeagueMembership` row → excluded at `PredictionRepository.cs:81`).

**Atomicity.** The match write and the scoring write are two separate transactions
(`MatchesController.cs:121` then `:124`), acknowledged in `ScoringTrigger.cs:7-11`: a
scoring failure leaves a saved result with stale points until someone calls
`POST /api/matches/{id}/rescore`. Within scoring itself, `SetAwardedPointsAsync` is a
single `SaveChangesAsync`, so per-match scoring is atomic.

### 3. Oracle Ledger

Every semantic Phase 1 needs, graded by how independent the source is from the code.
**Tier A** = a decision recorded with a rationale (usable as oracle). **Tier B** =
the document restates the implementation's shape (weak — cannot refute a bug in that
shape). **Tier C** = unresolved.

| # | Question | Verdict | Source | Tier |
|---|---|---|---|---|
| 1 | Which parameters exist, what each means | Six, defined individually | `scoring-engine-standings/plan.md:14,102-105` | A |
| 2 | Own goals — credited to whom, do they score | Credited team; count like any goal; expressed as the (player, team) pair | `.../plan.md:15,68,264` | A |
| 3 | First scorer — first only, tie-break | Earliest qualifying goal; both halves of the pair must match | `.../plan.md:90`, `plan-brief.md:25` | A |
| 3b | Same-minute tie-break keys | `(Minute, MinuteExtra ?? 0, MatchEventTypeId, PlayerId)`; `MatchEvent.Id` must never participate | `.../plan.md:92` | **B** for the surrogate keys, **A** for excluding the Id |
| 4 | Card counting | Exact equality; match totals; yellow/red split by `Code`; no second-yellow concept | `.../plan.md:68,90,105` | A |
| 5 | Correct outcome | Sign comparison; predicted draw matches actual draw | `.../plan.md:103` | **B** (restates the formula) |
| 5b | Does exact score also award correct outcome | **Yes — cumulative.** Rules stack; each configured rule means what the organizer's editor said | `.../plan.md:98`, `plan-brief.md:23` | A |
| 6 | Exact score implies outcome points | No bundling; only via separately configured `CorrectOutcome` | `.../plan.md:98,107` | A |
| 7 | Missing granular detail | No degrade (free Events endpoint carries scorers + cards); missing events score as zero, one recompute fixes a late entry | `roadmap.md:145,231`, `plan-brief.md:29` | A |
| 8 | Zero / negative points | **Forbidden.** `1..1000`; zero is no longer "does not score" — leave the parameter out | `archive/2026-08-03-custom-scoring-rules/plan.md:114,118` | A |
| 9 | Duplicate parameters | Rejected at validation **and** blocked by a unique index on `(LeagueId, Parameter)` | `.../custom-scoring-rules/plan.md:14,116` | A |
| 10 | Standings ranking | Points desc, then display name; shared rank on ties with skips; current members only, zero-prediction members included | `scoring-engine-standings/plan.md:287,297`, `plan-brief.md:32` | A |
| 11 | Per-half scorer prediction | No such concept exists in the product | corpus-wide search, zero hits | n/a |

**Both Tier B lines are verified in code, so they are safe to *use* — but a test must
not treat them as proof of correctness.** For #5 the sign formula and the documented
behaviour agree, and the behaviour is independently derivable from FR-011 ("scores per
the league's rules") plus ordinary football semantics, so assert the *behaviour*
(home win / draw / away win classification) rather than the formula. For #3b, assert
determinism and Id-independence, not the identity of the same-minute winner.

**Independent verification performed this session**: points validation `1..1000` at
`LeaguesController.cs:21-22,378`; unique index at `ScoringRuleConfiguration.cs:16`.
Both confirmed present.

### 4. Reachability filter — what NOT to test

The engine is more permissive than the product. These are engine tolerances with no
reachable production path, and asserting them would encode forbidden states as
behaviour:

| Engine tolerance | Blocked by | Verdict |
|---|---|---|
| `Points = 0` contributes 0 | `LeaguesController.cs:378` rejects `< 1` | Do not test as behaviour |
| Negative `Points` drives a negative total (no floor, `PredictionScorer.cs:24-29`) | same validation | Do not test as behaviour |
| Duplicate parameter double-counts (no dedup, `:24-29`) | validation + unique index `ScoringRuleConfiguration.cs:16` | Do not test as behaviour |
| `MatchOutcome.FromMatch` throws on a null final score (`:37-40`) | caller gates on `Status == Finished` (`MatchScoringService.cs:67-79`) | Defensive-only; see Open Question 2 |

Note the asymmetry: `PredictionScorer` is `public` Domain API, so it *can* be called
with any rule set. The argument for skipping these is that they are not product
behaviour, not that they are impossible.

### 5. Test surface for Phase 1

**Surface A — `PredictionScorer.Score` + `MatchOutcome.FromMatch`** (risk #1). Pure,
zero-dependency. Construct a `Prediction`, a `Match` with `MatchEvent`s, a
`MatchEventType` dictionary, and a rule list. Covers: each of the six predicates, the
`MissedPenalty` exclusion, own-goal-as-pair, blank-prediction cases,
no-events-means-zero, cumulative stacking, and determinism of first-scorer resolution.

**Surface B — `MatchScoringService.ScoreMatchAsync` with faked repositories** (risk #2).
Still a unit test: `IMatchRepository`, `IPredictionRepository`, `ILeagueRepository`,
`IMatchEventTypeRepository` and `ILogger` are all constructor-injected
(`MatchScoringService.cs:36-48`). Covers: two leagues on one tournament with contrasting
rule sets receive different totals for an identical forecast; a league with no rules
scores 0; an unfinished match nulls every awarded point.

**Explicitly out of Phase 1** (belongs to later phases per `test-plan.md` §3):
`ListStandingsAsync`'s SQL aggregation and `StandingsController.Rank` (risk #7 → Phase 3),
the write-path Added-vs-Modified defect (risk #3 → Phase 2), and everything requiring a
real database.

### 6. Unit-test stack (.NET 10)

Repo facts: all five projects target `net10.0` with `Nullable` and `ImplicitUsings`
enabled; no `Directory.Build.props`, `Directory.Packages.props`, `global.json`, or
`NuGet.config` anywhere — **central package management is not in use**, so a test project
declares versions inline. `prediction-league.slnx` lists five projects and no test project.
`.github/workflows/deploy-backend.yml` restores, publishes and scripts migrations — it has
**no `dotnet test` step**, so CI needs a new step added, not edited.

Recommended shape:

- Project `PredictionLeague.Domain.Tests` (`net10.0`, `Nullable=enable`,
  `ImplicitUsings=enable`, `OutputType=Exe`, `IsPackable=false`), added to
  `prediction-league.slnx` as a `<Project Path=... />` entry.
- `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Shouldly`, plus a
  `ProjectReference` to the layer under test.
- Run with `dotnet test src/server/prediction-league.slnx`.

Two decisions worth carrying into the plan verbatim:

- **Stay on VSTest.** With no `global.json`, `dotnet test` on .NET 10 defaults to VSTest.
  Do not copy the `xunit.v3.mtp-v2` / `UseMicrosoftTestingPlatformRunner` snippets from
  xUnit's getting-started docs — those assume the Microsoft.Testing.Platform template,
  which needs a `global.json` switch this repo does not have.
- **Not FluentAssertions v8+.** v8 and later require a paid licence for commercial use
  (v7 remains open source). Shouldly (MIT) is the recommendation; AwesomeAssertions
  (an MIT fork of FluentAssertions v7) is the near-drop-in alternative if the team wants
  that syntax.

Surfaced version numbers (xunit.v3 4.0.0, xunit.runner.visualstudio 4.0.0,
Microsoft.NET.Test.Sdk 18.9.0, Shouldly 4.3.0, AwesomeAssertions 9.5.0) came from
nuget.org via web search on 2026-08-31, with the VSTest-default and FluentAssertions
licensing facts confirmed against Context7. **Re-verify the exact versions with
`dotnet add package` at implementation time** rather than pinning them from this document.

## Code References

Local paths are relative to the repo root; prepend the permalink base above for GitHub links.

- `src/server/PredictionLeague.Domain/Scoring/PredictionScorer.cs:15-32` — the whole engine; entry point, null guards, summation loop
- `src/server/PredictionLeague.Domain/Scoring/PredictionScorer.cs:38-67` — `Awards`, the six predicates and the unknown-parameter default
- `src/server/PredictionLeague.Domain/Scoring/MatchOutcome.cs:30-77` — `FromMatch`; goal filtering, first-scorer ordering, card counts
- `src/server/PredictionLeague.Domain/Scoring/MatchOutcome.cs:52-64` — the ordering tuple and the rationale for excluding `MatchEvent.Id`
- `src/server/PredictionLeague.Domain/Entities/Enums.cs:26-34` — all six `ScoringParameter` values
- `src/server/PredictionLeague.Domain/Entities/Prediction.cs:14-33` — forecast shape
- `src/server/PredictionLeague.Infrastructure/Scoring/MatchScoringService.cs:50-104` — orchestration
- `src/server/PredictionLeague.Infrastructure/Scoring/MatchScoringService.cs:89` — **the risk #2 divergence point**
- `src/server/PredictionLeague.Infrastructure/Scoring/MatchScoringService.cs:109-119` — per-scope rule memoisation
- `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs:43-49` — rules loaded per tournament
- `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/PredictionRepository.cs:76-101` — standings aggregation
- `src/server/PredictionLeague.Api/Controllers/StandingsController.cs:69-89` — rank assignment with ties and skips
- `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:21-22,378` — points constrained to `1..1000`
- `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/ScoringRuleConfiguration.cs:16` — unique index on `(LeagueId, Parameter)`
- `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/MatchEventTypeConfiguration.cs:21-26` — the six seeded event types, including `MissedPenalty` under `Category = Goal`
- `src/server/PredictionLeague.Api/Scoring/ScoringTrigger.cs:7-40` — post-commit scoring and its swallowed failures
- `.github/workflows/deploy-backend.yml:54-75` — CI server steps; no test step

## Architecture Insights

- **The engine was designed to be testable and it shows.** A pure static function, a
  pure factory, and impurity pushed into a Scoped service behind four interfaces. The
  reason the server has no unit tests is not that the code resists them.
- **Ordering decisions are load-bearing and self-documented.** Excluding `MatchEvent.Id`
  from the sort and sorting in memory rather than in SQL both carry inline rationales
  (`MatchOutcome.cs:52-55`). These are the lines most worth protecting with a test,
  because their reasons are invisible to anyone editing them later.
- **Validation lives in the Api layer, not the Domain.** The engine trusts its inputs;
  `LeaguesController` enforces the range. Sound today, but it means the Domain's contract
  is broader than the product's, which is exactly why the reachability filter above is
  needed.
- **`AGENTS.md` under-documents the domain.** It names four scoring parameters against the
  enum's six, and it still says "No test suite exists yet in either unit" — true for the
  server today, but stale on the client, which has Playwright wired (`test-plan.md` §4).
- **The known write-path defect is adjacent but not in scope.** `lessons.md` — "New
  children of a tracked parent need an explicit Add" — names `ReplaceScoringRulesAsync`
  as one of the three affected methods. That is risk #3 / Phase 2; Phase 1 must not
  absorb it.

## Historical Context (from prior changes)

- `context/changes/scoring-engine-standings/plan.md` — the single richest oracle source.
  Lines 14-16 (parameter set and event types), 68-92 (event filtering and ordering),
  98-107 (cumulative stacking, per-parameter meanings), 264 (own-goal criterion),
  287-297 (standings ordering and rank sharing).
- `context/changes/scoring-engine-standings/plan-brief.md:23,25,29,32` — the same decisions
  in one-line form, each with its rationale; the most quotable oracle statements.
- `context/archive/2026-08-03-custom-scoring-rules/plan.md:14,114-118` — points constrained
  to `1..1000`, superseding S-03's "zero means does not score"; duplicate and unknown
  parameters rejected; unique index on `(LeagueId, Parameter)`.
- `context/archive/2026-08-03-organizer-create-league/plan.md:159-161` — the superseded
  `0..1000` rule, retained here so nobody re-derives the old oracle from the older document.
- `context/foundation/roadmap.md:145,231` — PRD Open Question 2 (granular-detail fallback)
  resolved 2026-06-04: no degrade, the free Events endpoint carries scorers and cards.
- `context/changes/scoring-engine-standings/reviews/impl-review.md:73-83` — a caveat that
  matters for this phase's premise: manual acceptance checkmarks in that plan reflect user
  attestation rather than independently verified evidence. Criteria 2.3, 5.8 and 4.2-4.5 —
  the ones `test-plan.md` cites as "verified by hand only" — are the checks this phase and
  Phase 3 are meant to convert into automation.
- `context/foundation/lessons.md` — "League organizer identity is single-sourced on
  OrganizerUserId" (Phase 3 relevance) and "New children of a tracked parent need an
  explicit Add" (Phase 2 relevance). Neither constrains Phase 1.

## Related Research

None. This is the first `research.md` produced under the `test-plan.md` rollout; Phases 2-5
will each open their own change folder. The prior `plan.md` documents cited above are design
records, not research artifacts.

## Open Questions

Ordered by how much they block test authoring. 1 and 2 should be settled before the plan is
written; 3-5 can be recorded as accepted risk.

1. **Same-minute goals — assert the winner, or only determinism?** Research recommends
   asserting determinism and Id-independence, and *not* pinning which player wins, because
   `MatchEventTypeId`/`PlayerId` are surrogate keys with no football meaning
   (`MatchOutcome.cs:61-62`). Confirm, or state the intended tie-break as product behaviour.

2. **Is `MatchOutcome.FromMatch`'s throw on an unfinished match a contract worth testing?**
   It is unreachable from the only production caller (`MatchScoringService.cs:67-79`).
   Test it as a defensive guard on a public Domain API, or leave it?

3. **`NormalGoal` always outranks `OwnGoal` and `Penalty` in the same minute** — a
   consequence of ordering by `MatchEventTypeId` (`MatchOutcome.cs:61`). With minute-granular
   admin entry this is reachable. Is it acceptable, or does the ordering need a product
   decision? *(This is a possible defect, not a test-design question — if it needs fixing,
   that is a separate change.)*

4. **Own-goal `TeamId` semantics are enforced outside the Domain.** The oracle says the
   credited (benefiting) team (`plan.md:15,264`), and `CorrectGoalScorer` trusts
   `MatchEvent.TeamId` without inversion. A Phase 1 unit test can prove the pair rule, but
   not that admin entry populates `TeamId` correctly. Accept that gap, or raise it for a
   later phase?

5. **Unmapped event types are dropped silently** (`MatchOutcome.cs:44-48`) — no log, no
   error. Intended resilience, or a gap worth an assertion?

Also worth a decision, though not blocking: **`AGENTS.md` names four scoring parameters
where the enum has six.** Correcting it is a one-line fix that prevents the next agent from
building a truth table with a third of the surface missing.
