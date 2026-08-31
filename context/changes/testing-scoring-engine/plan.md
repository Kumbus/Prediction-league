# Scoring Engine Truth Table — Implementation Plan

## Overview

Test-plan §3 Phase 1. Stand up the server's first unit-test project, then prove two
things the product has so far verified only by hand:

- **Risk #1** — a member's awarded total equals the number derived from *their league's*
  rule configuration, and the match facts (first scorer, cards, own goals) are derived
  correctly from the event list.
- **Risk #2** — two leagues on one tournament, scoring one identical forecast, produce
  two different totals, each correct for its own configuration.

Both are unit-level: no database, no HTTP, no clock. The engine was built for this —
`PredictionScorer.Score` and `MatchOutcome.FromMatch` are pure statics, and
`MatchScoringService` takes all five dependencies through the constructor.

## Current State Analysis

**There is no server test infrastructure at all.** `prediction-league.slnx` lists five
projects, none a test project. No `global.json`, no `Directory.Build.props`, no
`Directory.Packages.props`, no `NuGet.config` — central package management is not in use,
so a test project declares package versions inline. Installed SDK is 10.0.400; all five
projects target `net10.0` with `Nullable` and `ImplicitUsings` enabled.

`.github/workflows/deploy-backend.yml` restores the solution, publishes API and Functions,
and scripts migrations. It has **no `dotnet test` step**, and it triggers only on
`push` to `main` (paths `src/server/**`) plus `workflow_dispatch` — there is no
PR-triggered server workflow.

The code under test:

- `PredictionScorer.Score(Prediction, MatchOutcome, IEnumerable<ScoringRule>) : int`
  (`PredictionScorer.cs:15-32`) — null-guards all three arguments, then sums `rule.Points`
  for every rule whose predicate holds. Six predicates at `:38-67`, unknown parameter
  falls through to `_ => false`.
- `MatchOutcome.FromMatch(Match, IReadOnlyDictionary<int, MatchEventType>)`
  (`MatchOutcome.cs:30-77`) — filters goal events, resolves the first scorer, counts cards.
- `MatchScoringService.ScoreMatchAsync` (`MatchScoringService.cs:50-104`) — the
  orchestration, and the only place per-league rule selection happens (`:89`).

**Six scoring parameters, not four.** `Enums.cs:26-34` defines `ExactScore`,
`CorrectOutcome`, `CorrectGoalScorer`, `CorrectCardCount`, `CorrectYellowCards`,
`CorrectRedCards`. `AGENTS.md` documents only the first four — verified stale this
session.

**Product constraints that bound the test surface** (both verified in code this session):

- `LeaguesController.cs:20-22,378` clamps `Points` to `1..1000` and rejects duplicate
  parameters in one rule set.
- `ScoringRuleConfiguration.cs:16` puts a unique index on `(LeagueId, Parameter)`.

## Desired End State

`dotnet test src/server/prediction-league.slnx` runs a green suite that fails if:

- any scoring parameter starts awarding when it should not, or stops awarding when it should;
- rules stop stacking cumulatively;
- a `MissedPenalty` starts counting as a goal;
- `MatchEvent.Id` enters the first-scorer ordering (which would move `CorrectGoalScorer`
  points between members on a no-op re-save);
- card counting drifts from exact match-total equality;
- two leagues on one tournament stop diverging for an identical forecast;
- a league with no configured rules stops scoring 0;
- an unfinished match stops un-scoring its predictions.

The CI build job runs that suite, so a red test blocks the deploy. `test-plan.md` §6.1
carries the pattern for writing the next scoring test, §4 names the real stack, and §3
Phase 1 reads `complete`.

### Key Discoveries

- **Risk #2 is not provable at `PredictionScorer` alone.** `Score` takes the rule set as
  an argument, so passing two rule sets and getting two totals is close to tautological.
  The failure risk #2 names lives at `MatchScoringService.cs:89` —
  `rulesByLeague.GetValueOrDefault(prediction.LeagueId, NoRules)` — against a dictionary
  built at `:114-115` from `ListByTournamentWithRulesAsync`, which is filtered by
  **TournamentId**, not LeagueId. That one line is the divergence point; everything else
  is shared (one `MatchOutcome`, one pure `Score`).
- **`MissedPenalty` is seeded with `Category = Goal`** (`MatchEventTypeConfiguration.cs:24`),
  so goal filtering must exclude it by `Code` and cannot trust the category
  (`MatchOutcome.cs:57-58`). This is a live wart, not a hypothetical.
- **Excluding `MatchEvent.Id` from the sort is a rationale-backed decision**
  (`MatchOutcome.cs:52-54`) — a replace-all save mints fresh Guids, so an Id tie-break
  would move points between members on a re-save. The strongest oracle line available and
  the one most worth a test, because its reason is invisible to anyone editing the sort.
- **Own goals carry no special case.** The forecast is a *pair* — `PredictedFirstScorerPlayerId`
  **and** `PredictedFirstScorerTeamId` — so "player X credited to team B" expresses an own
  goal naturally (`PredictionScorer.cs:50-58`). The Domain treats `MatchEvent.TeamId` as
  authoritative and never inverts it.
- **Card predictions are `int?` against `int` actual** — lifted equality, so a blank
  forecast is `false`, never an exception. Comparison is exact equality, not a band.
- **A league with no rules is not an error.** It gets `NoRules`, a static empty list
  (`MatchScoringService.cs:18`), and every prediction scores 0.
- **The repository interfaces are fat** — `ILeagueRepository` 13 members, `IMatchRepository`
  8, `IPredictionRepository` 5, each plus 6 inherited from `IRepository<T>`. Hand-rolling
  four fakes would mean ~38 stub members, most of them dead.
- **Package versions confirmed live on nuget.org this session**: xunit.v3 4.0.0,
  xunit.runner.visualstudio 4.0.0, Microsoft.NET.Test.Sdk 18.9.0, Shouldly 4.3.0,
  NSubstitute 6.2.0.

## What We're NOT Doing

- **Not testing engine tolerances the product forbids.** Zero points, negative points, and
  duplicate parameters in one rule set are all blocked upstream
  (`LeaguesController.cs:378`, `ScoringRuleConfiguration.cs:16`). The engine tolerates
  them; asserting them would encode forbidden states as behaviour.
- **Not pinning which player wins a same-minute tie.** The last two ordering keys
  (`MatchEventTypeId`, `PlayerId`, `MatchOutcome.cs:61-62`) are surrogate keys with no
  football meaning. We assert determinism and Id-independence instead.
- **Not resolving whether `NormalGoal` should outrank `OwnGoal` in the same minute.** That
  is a possible product defect surfaced by research, not a test-design question. If it
  needs fixing, it is a separate change.
- **Not correcting `AGENTS.md`.** Research flagged it as stale (four scoring parameters
  against the enum's six, plus a "no test suite exists" line this change makes wrong).
  Excluded from this change by scope decision — worth opening separately, because the next
  agent reading it will build a truth table missing a third of the surface.
- **Not touching standings.** `PredictionRepository.ListStandingsAsync` and
  `StandingsController.Rank` are risk #7 → test-plan Phase 3.
- **Not touching the write-path defect.** The tracked-parent / explicit-`Add` bug
  (`lessons.md`) is risk #3 → Phase 2.
- **No integration tests, no database, no `WebApplicationFactory`.** Phase 2 and 3 own those.
- **Not adding a PR-triggered workflow.** The gate goes into the existing build job;
  broader gate wiring is Phase 5.

## Implementation Approach

Two test surfaces, both DB-free, in one test project.

**Surface A** (phases 2–3) targets the pure Domain: `PredictionScorer.Score` and
`MatchOutcome.FromMatch`. Fixtures are plain object construction — a `Prediction`, a
`Match` with `MatchEvent`s, a `Dictionary<int, MatchEventType>` mirroring the seeded six,
and a `List<ScoringRule>`.

**Surface B** (phase 4) targets `MatchScoringService.ScoreMatchAsync` with NSubstitute
standing in for the four repositories. Still a unit test — the service's only impurity is
the four interfaces and `ILogger`.

**The oracle discipline is the point of this phase.** Every expected total is derived from
the league's rule configuration and ordinary football semantics, never from running the
engine and recording what it returned. Concretely: a test that expects 7 points must be
able to explain 7 as "5 for `ExactScore` plus 2 for `CorrectOutcome`, both configured by
this league" — written out in the test, not computed by the same logic the engine uses.

Research graded two oracle lines as **Tier B** (the source document restates the
implementation's shape rather than recording an independent decision):

- The `CorrectOutcome` sign formula → assert the *behaviour* (home win / draw / away win
  classification), which is independently derivable from FR-011 plus football semantics,
  not the formula.
- The same-minute tie-break keys → assert determinism and Id-independence, not the winner.

## Critical Implementation Details

**Stay on VSTest.** With no `global.json`, `dotnet test` on .NET 10 defaults to VSTest.
Do **not** copy the `xunit.v3.mtp-v2` / `UseMicrosoftTestingPlatformRunner` snippets from
xUnit's getting-started docs — those assume the Microsoft.Testing.Platform template, which
needs a `global.json` switch this repo does not have. xunit.v3 still requires
`<OutputType>Exe</OutputType>` even on the VSTest path.

**Not FluentAssertions.** v8 and later require a paid licence for commercial use. Shouldly
(MIT) is the choice here.

**The event-type dictionary must mirror the seed exactly.** `MatchOutcome.FromMatch` looks
types up by `MatchEventTypeId`, and the ids are stable seeded values
(`MatchEventTypeConfiguration.cs:21-26`): 1 NormalGoal, 2 OwnGoal, 3 Penalty, 4
MissedPenalty (all `Category = Goal`), 5 YellowCard, 6 RedCard (both `Category = Card`).
A fixture that invents its own ids will pass while testing nothing real.

## Phase 1: Test project and CI gate

### Overview

Create the test project, wire it into the solution, and add the CI step — before any real
test exists, so the gate is live from the first assertion onward.

### Changes Required:

#### 1. Test project

**File**: `src/server/PredictionLeague.Tests/PredictionLeague.Tests.csproj`

**Intent**: A single test project covering both surfaces. Domain tests and Infrastructure
tests live in separate folders inside it rather than separate projects — the repo has zero
test projects today and five product projects, and one package set is cheaper to keep in
sync than two.

**Contract**: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`,
`OutputType=Exe` (required by xunit.v3), `IsPackable=false`. `PackageReference`s:
`xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Shouldly`,
`NSubstitute`. `ProjectReference`s to `PredictionLeague.Domain` and
`PredictionLeague.Infrastructure` (the latter transitively brings `Application`).

Add packages with `dotnet add package` rather than pinning versions by hand, then confirm
the resolved versions match those recorded under Key Discoveries. If a resolved major
differs, stop and flag it — a different xunit.v3 major changes the runner contract.

#### 2. Solution entry

**File**: `src/server/prediction-league.slnx`

**Intent**: Register the test project so a solution-wide restore, build and test sees it.

**Contract**: One `<Project Path="PredictionLeague.Tests/PredictionLeague.Tests.csproj" />`
entry appended to the existing five.

#### 3. Folder skeleton and one smoke test

**File**: `src/server/PredictionLeague.Tests/Domain/Scoring/PredictionScorerTests.cs`

**Intent**: Prove the runner is wired end to end before investing in fixtures. One trivial
test that exercises `PredictionScorer.Score` with an empty rule set and expects 0 — a real
assertion (a league with no rules scores nothing), not a placeholder to delete later.

**Contract**: Namespace `PredictionLeague.Tests.Domain.Scoring`. Folders `Domain/Scoring/`
and `Infrastructure/Scoring/` established here; the Infrastructure folder fills in phase 4.

#### 4. CI gate

**File**: `.github/workflows/deploy-backend.yml`

**Intent**: Run the suite in the existing `build` job so a red test blocks the deploy. The
workflow triggers only on push to `main`, so PRs stay ungated until Phase 5 — an accepted
gap for now, recorded here so it is not mistaken for coverage.

**Contract**: A `Run tests` step in the `build` job, after `Restore` and before
`Publish API`, invoking `dotnet test src/server/prediction-league.slnx --no-restore`.
Adding the step must not change the existing restore/publish/migrate ordering.

### Success Criteria:

#### Automated Verification:

- Solution restores: `dotnet restore src/server/prediction-league.slnx`
- Solution builds: `dotnet build src/server/prediction-league.slnx`
- Test suite runs and is green: `dotnet test src/server/prediction-league.slnx`
- Test discovery finds the smoke test (run output reports 1 passed, not 0 total)

#### Manual Verification:

- Resolved package versions match those recorded under Key Discoveries; any major-version
  difference is flagged rather than absorbed
- The CI step is positioned after `Restore` and before `Publish API`, and the migrate job's
  dependency on `build` is unchanged

**Implementation Note**: After completing this phase and all automated verification passes,
pause here for manual confirmation from the human before proceeding.

---

## Phase 2: `PredictionScorer` truth table (risk #1)

### Overview

Prove each of the six scoring parameters awards exactly when the league's rules say it
should — and stays silent when they do not. This is the core of risk #1.

### Changes Required:

#### 1. Fixture builders

**File**: `src/server/PredictionLeague.Tests/Domain/Scoring/ScoringFixtures.cs`

**Intent**: Shared construction helpers so the theories stay readable: build a
`Prediction`, build a `MatchOutcome` directly (phase 2 does not need `FromMatch`), and
build a rule list from `(parameter, points)` pairs.

**Contract**: Static helpers returning `Prediction`, `MatchOutcome` and
`IReadOnlyList<ScoringRule>`. Point values are supplied by the caller — the fixture must
never carry default point values, or a test could pass while reading a number the league
never configured.

#### 2. Per-parameter award / no-award theories

**File**: `src/server/PredictionLeague.Tests/Domain/Scoring/PredictionScorerTests.cs`

**Intent**: One `[Theory]` per scoring parameter, each covering both the awarding case and
at least one non-awarding case, with the expected total stated as a literal traceable to
the rule configuration.

**Contract**: Six theories against `ScoringParameter.ExactScore`, `CorrectOutcome`,
`CorrectGoalScorer`, `CorrectCardCount`, `CorrectYellowCards`, `CorrectRedCards`.

Behaviour each must pin, from the oracle rather than the predicate:

- `ExactScore` — awards only when both predicted scores equal both actual scores. A
  correct-outcome-but-wrong-scoreline forecast awards nothing.
- `CorrectOutcome` — classify by result, not by formula: predicted home win against actual
  home win awards; predicted draw against actual draw awards; predicted home win against
  actual away win does not. Cover all three classes.
- `CorrectGoalScorer` — awards only when player id **and** credited team id both match.
  Right player wrong team, and wrong player right team, both award nothing.
- The three card parameters — exact equality against the match total, not a band and not
  `>=`.

#### 3. Cumulative stacking

**File**: same

**Intent**: Prove rules stack. A league configuring both `ExactScore` and `CorrectOutcome`
awards both to a member who nailed the scoreline — the exact-score rule does not bundle or
supersede the outcome rule.

**Contract**: A league with `ExactScore = 5` and `CorrectOutcome = 2` against a perfectly
predicted match totals 7. The test states 5 + 2 = 7 explicitly; it must not compute the
expected value by summing the rule list, which would mirror the engine's own loop.

#### 4. Blank and absent forecasts

**File**: same

**Intent**: The nullable-forecast edge cases — the ones a happy-path suite misses.

**Contract**: A `null` `PredictedFirstScorerPlayerId`, a `null`
`PredictedFirstScorerTeamId`, and `null` card predictions each award nothing rather than
throwing. A match with no qualifying goal (`FirstScorerPlayerId` null on the outcome)
awards no `CorrectGoalScorer` even to a member who predicted one. A member who predicted 0
cards against a match with 0 cards **does** award — zero is a correct answer, not an absent one.

#### 5. Own goal as a pair

**File**: same

**Intent**: Prove the own-goal case needs no special handling — it is expressed by
predicting a player alongside the *opposing* team.

**Contract**: A forecast naming player X credited to team B, against an outcome whose first
scorer is player X credited to team B, awards `CorrectGoalScorer`. The same player credited
to team A does not.

#### 6. Null-argument guards

**File**: same

**Intent**: `Score` null-guards all three arguments (`PredictionScorer.cs:20-22`); it is
public Domain API and the guards are part of its contract.

**Contract**: `ArgumentNullException` for a null prediction, a null outcome, and a null
rule list.

### Success Criteria:

#### Automated Verification:

- Suite is green: `dotnet test src/server/prediction-league.slnx`
- All six `ScoringParameter` enum members appear in the test file (no parameter silently
  uncovered): `grep -c` across the six names returns a hit for each
- Solution still builds: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification:

- Every expected total is traceable to a stated rule configuration, not computed by summing
  the rule list or by re-deriving the predicate — spot-check the stacking test in particular
- Each theory covers a non-awarding case, not only the awarding one
- No test asserts zero, negative, or duplicate point values

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 3: `MatchOutcome` derivation (risk #1)

### Overview

Prove the match facts the engine scores against are derived correctly from the event list.
This is where the `MissedPenalty` wart and the load-bearing ordering decision live.

### Changes Required:

#### 1. Seeded event-type dictionary fixture

**File**: `src/server/PredictionLeague.Tests/Domain/Scoring/ScoringFixtures.cs` (extend)

**Intent**: A dictionary mirroring the six seeded `MatchEventType` rows exactly, so tests
exercise the real ids and categories rather than invented ones.

**Contract**: Ids and categories per `MatchEventTypeConfiguration.cs:21-26` — 1 NormalGoal,
2 OwnGoal, 3 Penalty, 4 MissedPenalty (all `Category = Goal`), 5 YellowCard, 6 RedCard
(both `Category = Card`). Plus a `Match` builder taking scores, status and an event list.

#### 2. Goal filtering and the `MissedPenalty` exclusion

**File**: `src/server/PredictionLeague.Tests/Domain/Scoring/MatchOutcomeTests.cs`

**Intent**: Prove a missed penalty never resolves as the first scorer despite being seeded
under `Category = Goal`, and that `NormalGoal`, `OwnGoal` and `Penalty` all count.

**Contract**: A match whose earliest `Category = Goal` event is a `MissedPenalty` at minute
10, with a `NormalGoal` at minute 20, resolves the minute-20 scorer. A match containing
only a `MissedPenalty` resolves `FirstScorerPlayerId` and `FirstScorerTeamId` to null —
both halves null together. Separate cases confirm `OwnGoal` and `Penalty` each qualify.

#### 3. First-scorer determinism and Id-independence

**File**: same

**Intent**: The highest-value test in this phase. `MatchEvent.Id` must never influence the
result, because a replace-all save mints fresh Guids and an Id-sensitive ordering would
move `CorrectGoalScorer` points between members on a no-op re-save
(`MatchOutcome.cs:52-54`).

**Contract**: Two assertions, neither naming which player wins a same-minute tie:

- **Determinism** — the same event set, presented in a different collection order, yields
  the same `FirstScorerPlayerId` and `FirstScorerTeamId`.
- **Id-independence** — the same event set with every `MatchEvent.Id` replaced by a fresh
  `Guid.NewGuid()` yields the same first scorer. This is the assertion that fails if
  someone adds `.ThenBy(x => x.Event.Id)` to the ordering.

Also cover the two meaningful ordering keys: an earlier `Minute` wins, and a null
`MinuteExtra` sorts as 0 so a 90th-minute goal beats a 90+1 goal.

#### 4. Card counting

**File**: same

**Intent**: Card totals are match-wide, not per-team, and yellows and reds split by `Code`.

**Contract**: A match with yellows and reds across both teams reports `TotalCards` as the
full count, with `YellowCards` and `RedCards` splitting it. A match with no card events
reports 0 for all three — not null. No second-yellow concept exists; only `YellowCard` and
`RedCard` are seeded.

#### 5. Defensive contracts

**File**: same

**Intent**: Both are unreachable from the production caller (`MatchScoringService.cs:67-79`
gates on `Status == Finished` first), but `MatchOutcome` is public Domain API and these are
real contracts a future caller can hit.

**Contract**: `FromMatch` throws `ArgumentException` when `HomeScore` or `AwayScore` is
null — the guard exists so an unfinished match cannot silently become 0-0 and award
`ExactScore` to everyone who predicted a goalless draw. An event whose `MatchEventTypeId`
is absent from the dictionary is ignored: no throw, and it contributes to neither the goal
resolution nor the card counts.

### Success Criteria:

#### Automated Verification:

- Suite is green: `dotnet test src/server/prediction-league.slnx`
- Solution builds: `dotnet build src/server/prediction-league.slnx`
- The Id-independence test genuinely guards the ordering: temporarily append
  `.ThenBy(x => x.Event.Id)` to the ordering in `MatchOutcome.cs:59-64`, confirm the suite
  goes red, then revert

#### Manual Verification:

- No test asserts which player wins a same-minute tie
- The event-type fixture ids and categories match `MatchEventTypeConfiguration.cs:21-26`
  exactly
- The `MissedPenalty` test would fail if the `Code` exclusion were removed and the filter
  trusted `Category` alone

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 4: `MatchScoringService` per-league isolation (risk #2)

### Overview

Prove the product's wedge — per-league custom scoring — actually holds at the one line
where it can break. Everything else in the scoring path is shared.

### Changes Required:

#### 1. Substitute setup

**File**: `src/server/PredictionLeague.Tests/Infrastructure/Scoring/MatchScoringServiceTests.cs`

**Intent**: Stand up `MatchScoringService` with NSubstitute standing in for the four
repositories, stubbing only the methods the service actually calls. The interfaces carry far
more members than the service uses; hand-rolled fakes would mean ~38 mostly-dead stubs that
break the build every time a slice adds a method.

**Contract**: Four substitutes plus a logger. Methods to stub:
`IMatchRepository.GetWithEventsAsync`, `IPredictionRepository.ListForMatchAsync`,
`IPredictionRepository.SetAwardedPointsAsync`,
`ILeagueRepository.ListByTournamentWithRulesAsync`,
`IMatchEventTypeRepository.GetAllAsync`.

Points are asserted by capturing the `IReadOnlyDictionary<Guid, int?>` handed to
`SetAwardedPointsAsync` — the service computes only and never mutates a tracked entity, so
that dictionary is the whole observable output.

#### 2. Two leagues must diverge

**File**: same

**Intent**: The risk #2 assertion. Two leagues on one tournament, contrasting rule sets, one
identical forecast from one member in each — the totals must differ, and each must be
correct for its own league.

**Contract**: League A configures `ExactScore` only; league B configures `CorrectOutcome`
and `CorrectGoalScorer`. One match, one outcome, two predictions with identical field
values but different `LeagueId`. Assert both totals against numbers derived from each
league's own configuration — not merely that they differ, which a coincidence could satisfy
just as well as real isolation.

`ListByTournamentWithRulesAsync` returns both leagues, matching production: it is filtered
by TournamentId, and per-league selection happens in memory at `MatchScoringService.cs:89`.

#### 3. A league with no rules scores 0

**File**: same

**Intent**: The `NoRules` fallback (`MatchScoringService.cs:18,89`) is documented behaviour,
not an error and not a skip.

**Contract**: A third league on the same tournament with an empty `ScoringRules` collection
receives `0` for its prediction — an integer zero, never null. Zero means "scored, earned
nothing"; null means "not scored", and standings depend on the distinction.

#### 4. An unfinished match un-scores

**File**: same

**Intent**: Reverting a result must take its points with it (`MatchScoringService.cs:67-78`),
or standings keep asserting something the recorded result no longer says.

**Contract**: A match with `Status != Finished`, or `Finished` with a null score, produces
`null` for every prediction in the dictionary. Three cases: not finished, finished with null
`HomeScore`, finished with null `AwayScore`.

#### 5. Early-exit paths

**File**: same

**Intent**: Two guards worth pinning because both are silent.

**Contract**: A missing match (`GetWithEventsAsync` returns null) returns
`MatchScoringResult.None` and never calls `SetAwardedPointsAsync`. A match with no
predictions likewise returns `MatchScoringResult.None` without a write.

### Success Criteria:

#### Automated Verification:

- Suite is green: `dotnet test src/server/prediction-league.slnx`
- Solution builds: `dotnet build src/server/prediction-league.slnx`
- The divergence test genuinely guards line 89: temporarily change
  `rulesByLeague.GetValueOrDefault(prediction.LeagueId, NoRules)` to select a fixed league's
  rules, confirm the suite goes red, then revert

#### Manual Verification:

- The two-league test asserts each league's *specific* total, not merely that the two differ
- The zero-rule case asserts integer `0`, distinct from `null`
- Substitutes stub only the methods the service calls; no substitute is configured for a
  method that never runs

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 5: Cookbook and rollout sync

### Overview

The rollout is stateful — Phase 2 reads these files to know what landed. This phase is the
defined exit condition for Phase 1.

### Changes Required:

#### 1. Cookbook pattern

**File**: `context/foundation/test-plan.md` (§6.1)

**Intent**: Replace the `TBD — see §3 Phase 1` placeholder with the actual pattern, so the
next scoring test is written the same way without re-deriving it.

**Contract**: §6.1 must capture: where the test project lives and how to run it; the fixture
builders and the seeded event-type dictionary; how an expected total is derived from a rule
configuration rather than from the engine (risk #1's oracle constraint); the two-league
contrasting-configuration shape (risk #2); and the reachability filter — that zero, negative
and duplicate rule values are out of bounds because the product forbids them.

#### 2. Stack table

**File**: `context/foundation/test-plan.md` (§4)

**Intent**: The `unit (server)` row currently reads `none yet — see §3 Phase 1`.

**Contract**: Name xUnit v3 on VSTest, Shouldly, NSubstitute with their resolved versions
and a `checked:` date, and note the VSTest-not-MTP decision and its `global.json` reason.

#### 3. Rollout status

**File**: `context/foundation/test-plan.md` (§3)

**Intent**: Move Phase 1 to `complete`.

**Contract**: The Phase 1 row's Status cell reads `complete` (a fixed parser literal from
the §3 vocabulary). Phases 2–5 are untouched.

#### 4. Gate table

**File**: `context/foundation/test-plan.md` (§5)

**Intent**: The `unit (server)` gate reads "required after §3 Phase 1" — that condition is
now met.

**Contract**: Record that the gate is live in the `deploy-backend.yml` build job, and note
the known gap: the workflow triggers on push to `main`, so PRs are not yet gated. Phase 5
owns closing that.

#### 5. Change identity

**File**: `context/changes/testing-scoring-engine/change.md`

**Intent**: Stamp the change as implemented.

**Contract**: `status: implementing` → the terminal value used by this repo's convention on
completion; `updated:` set to the completion date.

### Success Criteria:

#### Automated Verification:

- No `TBD` remains in §6.1: `grep -n "TBD" context/foundation/test-plan.md` does not match
  the §6.1 block
- §3 Phase 1 Status reads `complete`
- §4 `unit (server)` row no longer reads `none yet`
- Full suite still green: `dotnet test src/server/prediction-league.slnx`

#### Manual Verification:

- §6.1 is specific enough that someone could write a new scoring test from it without
  re-reading this plan
- The oracle constraint is stated in §6.1, not just implied
- The PR-gating gap is recorded in §5 rather than left as an unstated assumption

---

## Testing Strategy

This change *is* the testing strategy for risks #1 and #2. What matters is the discipline:

### Oracle rules applied here

- Expected totals come from the league's rule configuration plus football semantics, never
  from running the engine and recording the result.
- Where research graded a source **Tier B** — the document restating the implementation's
  shape rather than recording an independent decision — we assert the behaviour, not the
  formula: `CorrectOutcome` is tested as home-win / draw / away-win classification, and the
  same-minute tie-break as determinism rather than a named winner.
- Where the sources do not resolve the expected behaviour, we do not guess. Two such cases
  were carried into "What We're NOT Doing" rather than answered by reading the code.

### Anti-patterns explicitly avoided

- **Mirror implementation** — the stacking test states 5 + 2 = 7 as a literal; it does not
  sum the rule list, which is what the engine's own loop does.
- **Happy paths only** — every parameter theory carries a non-awarding case, and the null
  and blank forecast cases are explicit.
- **Redundant copies** — one theory per parameter, not six near-identical facts.

### Mutation-style spot checks

Two success criteria deliberately break production code, confirm red, then revert. These are
the cheapest available proof that a test guards what it claims:

- Phase 3 — append `.ThenBy(x => x.Event.Id)` to the first-scorer ordering.
- Phase 4 — make line 89 select a fixed league's rules.

If either mutation leaves the suite green, that test is decorative and must be rewritten
before the phase is accepted. Running Stryker across the scoring module is a reasonable
follow-up once the suite exists, but it is not a gate for this phase.

### Manual testing steps

1. Run `dotnet test src/server/prediction-league.slnx` from a clean clone; confirm green.
2. Perform each of the two mutation spot checks; confirm red, then revert.
3. Read three expected totals at random and confirm each traces to a stated rule
   configuration rather than to the engine.

## Performance Considerations

None. The suite is pure in-memory unit tests with no I/O; it should run in well under a
second and adds negligible time to the CI build job.

## Migration Notes

Not applicable — no schema, no data, no production behaviour changes. The one production
file touched is `.github/workflows/deploy-backend.yml`, and the change is additive: a new
step in an existing job. Rollback is deleting the step and the project entry from
`prediction-league.slnx`.

## References

- Research: `context/changes/testing-scoring-engine/research.md`
- Test plan: `context/foundation/test-plan.md` §2 risks #1 and #2, §3 Phase 1, §6.1
- Oracle sources: `context/changes/scoring-engine-standings/plan.md:14-16,68-92,98-107,264,287-297`
  and `plan-brief.md:23,25,29,32`; `context/archive/2026-08-03-custom-scoring-rules/plan.md:14,114-118`
- Engine: `src/server/PredictionLeague.Domain/Scoring/PredictionScorer.cs:15-67`
- Outcome derivation: `src/server/PredictionLeague.Domain/Scoring/MatchOutcome.cs:30-77`
- Risk #2 divergence point: `src/server/PredictionLeague.Infrastructure/Scoring/MatchScoringService.cs:89`
- Seeded event types: `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/MatchEventTypeConfiguration.cs:21-26`
- Reachability bounds: `src/server/PredictionLeague.Api/Controllers/LeaguesController.cs:20-22,378`;
  `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/ScoringRuleConfiguration.cs:16`
- CI: `.github/workflows/deploy-backend.yml:47-75`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Test project and CI gate

#### Automated

- [x] 1.1 Solution restores: `dotnet restore src/server/prediction-league.slnx` — 7e9ed33
- [x] 1.2 Solution builds: `dotnet build src/server/prediction-league.slnx` — 7e9ed33
- [x] 1.3 Test suite runs and is green: `dotnet test src/server/prediction-league.slnx` — 7e9ed33
- [x] 1.4 Test discovery finds the smoke test (1 passed, not 0 total) — 7e9ed33

#### Manual

- [x] 1.5 Resolved package versions match Key Discoveries; major differences flagged — 7e9ed33
- [x] 1.6 CI step positioned after Restore, before Publish API; migrate job dependency unchanged — 7e9ed33

### Phase 2: `PredictionScorer` truth table (risk #1)

#### Automated

- [x] 2.1 Suite is green: `dotnet test src/server/prediction-league.slnx` — 79aa428
- [x] 2.2 All six `ScoringParameter` members appear in the test file — 79aa428
- [x] 2.3 Solution still builds: `dotnet build src/server/prediction-league.slnx` — 79aa428

#### Manual

- [x] 2.4 Every expected total traces to a stated rule configuration, not to the engine — 79aa428
- [x] 2.5 Each theory covers a non-awarding case — 79aa428
- [x] 2.6 No test asserts zero, negative, or duplicate point values — 79aa428

### Phase 3: `MatchOutcome` derivation (risk #1)

#### Automated

- [x] 3.1 Suite is green: `dotnet test src/server/prediction-league.slnx` — 5be4330
- [x] 3.2 Solution builds: `dotnet build src/server/prediction-league.slnx` — 5be4330
- [x] 3.3 Id-independence mutation check: adding `.ThenBy(x => x.Event.Id)` turns the suite red, then reverted — 5be4330

#### Manual

- [x] 3.4 No test asserts which player wins a same-minute tie — 5be4330
- [x] 3.5 Event-type fixture ids and categories match the seed exactly — 5be4330
- [x] 3.6 `MissedPenalty` test would fail if the `Code` exclusion were removed — 5be4330

### Phase 4: `MatchScoringService` per-league isolation (risk #2)

#### Automated

- [x] 4.1 Suite is green: `dotnet test src/server/prediction-league.slnx` — 9bc7c85
- [x] 4.2 Solution builds: `dotnet build src/server/prediction-league.slnx` — 9bc7c85
- [x] 4.3 Line 89 mutation check: fixing rule selection to one league turns the suite red, then reverted — 9bc7c85

#### Manual

- [x] 4.4 Two-league test asserts each league's specific total, not merely that they differ — 9bc7c85
- [x] 4.5 Zero-rule case asserts integer `0`, distinct from `null` — 9bc7c85
- [x] 4.6 Substitutes stub only the methods the service calls — 9bc7c85

### Phase 5: Cookbook and rollout sync

#### Automated

- [x] 5.1 No `TBD` remains in `test-plan.md` §6.1
- [x] 5.2 §3 Phase 1 Status reads `complete`
- [x] 5.3 §4 `unit (server)` row no longer reads `none yet`
- [x] 5.4 Full suite still green: `dotnet test src/server/prediction-league.slnx`

#### Manual

- [x] 5.5 §6.1 is specific enough to write a new scoring test from
- [x] 5.6 The oracle constraint is stated in §6.1, not implied
- [x] 5.7 The PR-gating gap is recorded in §5
