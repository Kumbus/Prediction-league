<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-02 Admin Seed Tournament

- **Plan**: context/changes/admin-seed-tournament/plan.md
- **Scope**: Full plan (Phases 1–5 of 5)
- **Date**: 2026-07-18
- **Verdict**: NEEDS ATTENTION → resolved (all 10 findings triaged 2026-07-19; see Triage Outcome)
- **Findings**: 1 critical, 3 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

Automated success criteria re-run green: server build 0 errors, client build ✅, lint 0 errors (1 warning — see F8). Manual rows 3.3–3.9 marked `[x]` passed against an out-of-band dev DB, not reproducible from code (F1) — rubber-stamp risk.

Not a finding: NU1903 (Microsoft.OpenApi 2.0.0 vuln) is pre-existing from F-03, not in this diff.

All Critical Implementation Details verified correct: admin claim-refresh ordering (UpdateAsync→RefreshSignInAsync), filtered unique index on ExternalApiId, publish gate as per-controller IsAdmin check, delete-cascade scoping + 409-on-league-reference, position enum stored as int append-only, additive/forward-only migration, apiFetch FormData handling, RequireAdmin mirrors RequireAuth.

## Findings

### F1 — Nationality seed (~250 rows) missing from code

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: NationalityConfiguration.cs:9-17 · migration 20260608214131:47-58
- **Detail**: Plan (Ph1 #6/#7) requires Nationality seeded via HasData (~250 ISO 3166-1 alpha-3 entries, stable ids). VERIFIED ABSENT: no HasData in config; migration only CreateTable("Nationalities") with no InsertData; snapshot has no seed. On a fresh/prod DB, GET /api/nationalities returns [], client dropdown empty, every CSV row → "Unknown NationalityCode" conflict → zero players importable. Progress rows 3.3/3.5 passed only because the dev DB was seeded out-of-band.
- **Fix**: Add HasData(250 rows) to NationalityConfiguration + a new additive migration emitting InsertData. Forward-only; safe on F-04 prod (empty table today).
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F2 — Unfiltered unique index + ExternalPlayerId ?? 0 collide

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability/Data safety)
- **Location**: PlayerConfiguration.cs:18 · CsvHelperPlayerImporter.cs:117 · PlayersController.cs:120
- **Detail**: Plain `HasIndex(p => p.ExternalPlayerId).IsUnique()` (no filter); both create paths coerce missing id to 0. Two players without an external id both write 0 → unique violation → unhandled 500. In CSV import the single-transaction commit rolls back entirely, so a realistic seed CSV lacking ExternalPlayerId can never import. The `if (ext.HasValue)` pre-check (importer:93) skips zero rows. With F1, defeats the slice's primary purpose.
- **Fix**: Make the index filtered (`WHERE [ExternalPlayerId] <> 0`, or nullable column + `IS NOT NULL`) via migration; stop coercing to 0. Fold migration in with F1.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F3 — CSV parse boundary has no error handling → opaque 500

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability) / Pattern
- **Location**: PlayersController.cs:159-183 · CsvHelperPlayerImporter.cs:155-167
- **Detail**: No try/catch around GetRecords<PlayerCsvRow>().ToList(). Malformed CSV throws CsvHelperException → unhandled 500. Sibling IngestController.cs:32-46 wraps its boundary and maps to 400/502 ProblemDetails.
- **Fix**: Catch CsvHelper/IO exceptions and return 400 ProblemDetails, matching IngestController.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F4 — Per-row N+1 queries in CSV import

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Performance)
- **Location**: CsvHelperPlayerImporter.cs:69-150
- **Detail**: Loop issues FindByNameAndNationalityAsync (:91), GetByExternalPlayerIdAsync (:95), squads.ExistsAsync (:141) per row — 2-3 round-trips × up to thousands of rows. Nationalities pre-loaded once (:60); players/squads not. Plan's perf note assumed ~700 rows under a second — sequential, won't hold.
- **Fix**: Pre-load existing players (by name+nationality and by external id) and squad keys into dictionaries once, mirroring :60.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F5 — PATCH players skips ExternalPlayerId collision check

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: PlayersController.cs:142
- **Detail**: PATCH assigns ExternalPlayerId with no uniqueness pre-check, unlike Create (:109-114). Patching to an id owned by another player throws DbUpdateException → 500 instead of 409.
- **Fix**: Reuse Create's collision pre-check.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F6 — Admin promotion is one-way (removal never revokes)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Security)
- **Location**: AuthController.cs:187-198
- **Detail**: EnsureAdminClaimAsync sets IsGlobalAdmin=true but nothing clears it when an email is removed from Admin:Emails. A demoted email keeps global admin permanently.
- **Fix**: Flip IsGlobalAdmin false when !IsAdmin(email), or document de-promotion as manual.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F7 — Client data-loads without error handling

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PlayerImportPage.tsx:23 · PlayerFormPage.tsx:29 · PlayersListPage.tsx:19
- **Detail**: Effects fetch /api/tournaments / /api/nationalities with no try/catch; failed request rejects silently — inconsistent with TournamentsListPage/detail/form pages that set an error state.
- **Fix**: Wrap in try/catch, surface an error like siblings.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F8 — useEffect missing 'reload' dependency (lint warning)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency / Success Criteria
- **Location**: TournamentDetailPage.tsx:41
- **Detail**: react-hooks/exhaustive-deps warning: useEffect missing 'reload' dep. Build/lint pass with 0 errors, but it's a real warning.
- **Fix**: Add reload to deps or wrap in useCallback.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F9 — UI-fidelity drift vs Phase 5 plan

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: TournamentsListPage.tsx:46 · TournamentFormPage.tsx · PlayerFormPage.tsx · PlayersListPage.tsx:67-73
- **Detail**: Delete uses window.confirm/alert not shadcn Dialog; forms are plain <form> not shadcn Form; players list omits Club / National Team columns. Functionally correct.
- **Fix**: Adopt shadcn Dialog/Form primitives; add the two columns — or accept as v1 and note.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

### F10 — NationalityRepository.GetByCodeAsync non-sargable

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Performance)
- **Location**: NationalityRepository.cs:17
- **Detail**: `n.Code.ToUpper() == code.ToUpper()` is non-sargable, skips IX_Nationalities_Code. Low impact (tiny table, off hot path — importer uses in-memory dict).
- **Fix**: Use case-insensitive collation / EF.Functions.Collate.
- **Decision**: RESOLVED — see Triage Outcome (2026-07-19)

## Triage Outcome (2026-07-19)

All 10 findings triaged with the user.

| ID | Decision |
|----|----------|
| F1 | **Fixed differently** — nationality seed kept as idempotent SQL, documented as a required deploy step (`src/server/db/README.md`, `change.md`). |
| F2 | **Fixed** — filtered unique index `WHERE [ExternalPlayerId] <> 0` (migration `FilterPlayerExternalIdIndex`); 0 no longer collides. |
| F3 | **Fixed** — `CsvImportException` thrown at the parse boundary in both importers; controllers map to 400 ProblemDetails. |
| F4 | **Fixed** — importer pre-loads players (name+nationality, external id) and squad ids into dictionaries; per-row DB round-trips removed. |
| F5 | **Fixed** — PATCH runs the same ExternalPlayerId collision pre-check as Create (409 on conflict). |
| F6 | **Fixed** — `EnsureAdminClaimAsync` reconciles both ways: promotes when listed, revokes when not. |
| F7 | **Fixed** — data-load effects in PlayerImport/PlayerForm/PlayersList wrapped in try/catch surfacing an error. |
| F8 | **Fixed** — `reload` wrapped in `useCallback([id])`; exhaustive-deps satisfied. |
| F9 | **Partially fixed** — Club/National-team columns added to the players list; shadcn Dialog/Form conversion deferred (needs a new radix dependency + form refactor). |
| F10 | **Fixed** — `GetByCodeAsync` uses plain sargable equality (Code column is CI collation), hits `IX_Nationalities_Code`. |
