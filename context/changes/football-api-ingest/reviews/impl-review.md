<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: F-03 Football API Ingest

- **Plan**: context/changes/football-api-ingest/plan.md
- **Scope**: All 5 phases (full plan)
- **Date**: 2026-06-07
- **Verdict**: NEEDS ATTENTION → fixes applied (see Decisions)
- **Findings**: 0 critical, 4 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Build: `dotnet build prediction-league.slnx` → 0 warnings, 0 errors (verified post-fix).
Manual criteria 4.2 / 4.3 / 5.4 remain unverified — blocked by the documented API-Football free-tier season gate, not by code.

## Findings

### F1 — MapStatus dumps postponed/cancelled into Live → quota burn

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason
- **Dimension**: Safety & Quality
- **Location**: FixtureIngestService.cs:267-272 (+ :122)
- **Detail**: catch-all `_ => Live` sent PST/CANC/ABD/AWD/WO to Live; line 122 then spent a /fixtures/events call on each dead fixture (100/day cap) and re-polled them forever. Plan itself flawed ("everything else → Live").
- **Fix**: Added explicit arm mapping NS/TBD/PST/CANC/ABD/AWD/WO/SUSP/INT → Scheduled; `_ => Live` for true in-play only. Stays 3-bucket.
- **Decision**: FIXED

### F2 — No per-minute pacing; MinuteRemaining captured but unused

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason
- **Dimension**: Safety & Quality
- **Location**: FixtureIngestService.cs:122-138
- **Detail**: only daily quota guarded; RateLimitSnapshot.MinuteRemaining never read. 10 req/min free cap → busy slate hits 429; Polly retries 2× then throws, aborting the whole run.
- **Fix**: Added `MinMinuteBuffer` + minute-remaining tracking; guard now stops event calls when daily OR minute remaining is low.
- **Decision**: FIXED

### F3 — Vendor wire-shape DTOs leak into Application boundary

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes
- **Dimension**: Architecture
- **Location**: Application/Abstractions/Football/FixturesResponse.cs, EventsResponse.cs
- **Detail**: fixture/event DTOs with `[JsonPropertyName]` (vendor JSON) lived in Application; plan placed them in Infrastructure/Football/Dtos/. IFootballApiClient stayed coupled to the vendor wire shape.
- **Fix (Fix A)**: Flattened provider-neutral records (IngestFixture/IngestEvent/IngestTeamRef) in Application; moved JSON-attributed wire DTOs (FixtureItem/EventItem/… in FixtureWire.cs + EventWire.cs) into Infrastructure as `internal`; FootballApiClient now maps wire → neutral. Service updated to neutral shape. Build green.
- **Decision**: FIXED (Fix A)

### F4 — Migration reinterprets existing data + Guid.Empty FK defaults

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; narrowly scoped
- **Dimension**: Safety & Quality (Data safety)
- **Location**: 20260607113246_AddFootballIngestModel.cs:16-87
- **Detail**: RenameColumn Type→MatchEventTypeId, HomeTeam→Round reinterpret old enum/string data as FK ids/round labels; new non-null FKs added with defaultValue Guid.Empty then Restrict FKs created. Empty dev DB = fine. Existing rows = FK step fails / dangling Guid.Empty.
- **Fix**: No code change — acceptable per plan (no prod data). Caveat queued into follow-ups/review-fixes.md for F-04 release steps.
- **Decision**: NOTED (follow-up for F-04)

### F5 — IngestController has no try/catch (raw 500s)

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: IngestController.cs:34-35
- **Detail**: InvalidOperationException (not found / no ExternalApiId) + FootballApiException surfaced as raw 500, no ProblemDetails.
- **Fix**: Added try/catch → InvalidOperationException = 400, FootballApiException = 502, via ProblemDetails.
- **Decision**: FIXED

### F6 — Quota guard bypassed when daily header absent

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: FixtureIngestService.cs:124
- **Detail**: guard is `quotaRemaining is not null && <= buffer`; null header bypasses it. By design (can't guard unknown).
- **Decision**: SKIPPED

### F7 — No ApiKey validation at startup

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: DependencyInjection.cs:45-54
- **Detail**: AddFootballIngest does not validate ApiFootball:ApiKey; empty key only fails later as an envelope error.
- **Decision**: SKIPPED
