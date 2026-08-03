<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Admin seed tournament — manual match entry (inline)

- **Plan**: context/changes/admin-seed-tournament/plan.md
- **Scope**: manual-match slice (uncommitted diff, folded into admin-seed-tournament)
- **Date**: 2026-07-19
- **Verdict**: NEEDS ATTENTION → resolved (all findings triaged)
- **Findings**: 0 critical  2 warnings  3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING (feature was absent from plan.md; addendum added) |
| Scope Discipline | PASS (user-requested, documented in change.md) |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING (magic number; fixed) |
| Success Criteria | PASS (server + client build green; migration applied live) |

## Findings

### F1 — Manual-match slice absent from plan.md

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence / Scope Discipline
- **Location**: context/changes/admin-seed-tournament/plan.md
- **Detail**: The whole manual-match feature is EXTRA relative to plan.md (ingest-only). Documented in change.md, user-requested, but plan.md was stale.
- **Fix**: Added a dated addendum block to plan.md pointing at change.md Notes.
- **Decision**: FIXED

### F5 — Inline CSV size cap vs. extracted const

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: TournamentsController.cs (ImportMatches attributes)
- **Detail**: `2 * 1024 * 1024` inline while PlayersController extracts `CsvSizeCapBytes`.
- **Fix**: Added `private const long CsvSizeCapBytes` to TournamentsController; both attributes reference it.
- **Decision**: FIXED

### F2 — Match CSV import had no dedup (re-upload duplicated)

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🔎 MEDIUM
- **Dimension**: Reliability
- **Location**: CsvHelperMatchImporter.cs
- **Detail**: Manual matches carry NULL ExternalFixtureId, so every row was a Create — re-uploading the same file duplicated matches.
- **Fix**: Load the tournament's existing matches once, dedup on `(homeTeamId, awayTeamId, kickoff-unix-seconds)`; duplicates (vs DB and vs earlier rows in the same file) land in Conflicts and are skipped.
- **Decision**: FIXED

### F3 — No DB uniqueness on Team.Name

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Data safety
- **Location**: TeamConfiguration.cs
- **Detail**: App-level case-insensitive dedup only; no unique index on Name, so a race could admit duplicate-named teams. Acceptable for single-admin MVP.
- **Decision**: SKIPPED

### F4 — Migration Down() would fail after manual rows exist

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Data safety
- **Location**: 20260719103717_AddManualMatchEntry.cs
- **Detail**: Down() recreates NON-filtered unique indexes; SQL Server allows one NULL per unique index, so rollback fails once ≥2 manual matches exist. Forward-only policy means Down is never run.
- **Decision**: SKIPPED
