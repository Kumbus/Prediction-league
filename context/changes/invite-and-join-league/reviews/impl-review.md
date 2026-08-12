<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Invite and join a league (S-05)

- **Plan**: `context/changes/invite-and-join-league/plan.md`
- **Scope**: Phases 1-4 of 4 (full plan)
- **Date**: 2026-08-12
- **Verdict**: NEEDS ATTENTION → RESOLVED after triage (all 4 warnings fixed)
- **Findings**: 0 critical, 4 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

Plan-drift audit found no DRIFT, MISSING, or EXTRA across all four phases, and no
violation of the plan's "What We're NOT Doing" guardrails. The details most likely to go
wrong were all honored: the DI cycle avoided via the `nextCode` delegate, tracked-vs-detached
guards on every write, the membership count read off the tracked graph rather than a second
query, and `returnUrl` staying pinned to `/sign-in`.

## Findings

### F1 — Concurrent transfers can leave two Organizer rows

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs:161-176`
- **Detail**: No concurrency token on `League` or `LeagueMembership`. Two `PUT /organizer` calls
  that both read before either commits: the second demotes only the organizer its stale snapshot
  saw, so the first winner keeps `Role = Organizer` while `OrganizerUserId` points at the second
  target — two Organizer rows, one disagreeing with the league.

  The sub-agent rated this CRITICAL; downgraded after verifying blast radius. Every `Role` usage
  server-side was grepped: authorization never reads it — `isOrganizer` is computed from
  `OrganizerUserId` in all five routes. `Role` reaches only the roster DTO, so the visible damage
  is a wrong badge for one member, not an authorization hole. It also requires one organizer
  firing two *different* targets within the same few ms; a double-click on the same target is
  idempotent. The plan scoped concurrency handling to join only, and its actual stated invariant
  ("one save each") does hold.
- **Fix A ⭐ Recommended**: Leave as-is, record as a lesson.
  - Strength: Honest about cost/benefit at friend-group scale; the invariant that matters for
    access control (`OrganizerUserId`) is already single-sourced.
  - Tradeoff: A rare wrong badge stays possible until someone adds a concurrency token.
  - Confidence: HIGH — `Role` usage verified exhaustively by grep.
  - Blind spot: If a later slice authorizes off `Role`, this becomes a real hole — which is
    exactly what the lesson would catch.
- **Fix B**: Add a `RowVersion` concurrency token to `League`.
  - Strength: Makes the stale transfer fail loudly instead of writing.
  - Tradeoff: Schema change + migration + a 409 path to design, for a race that costs a badge.
  - Confidence: MEDIUM — straightforward but untested here, and it changes every `League` write
    path, not just transfer.
  - Blind spot: Interaction with the invite-code retry, which re-saves a tracked graph.
- **Decision**: FIXED + ACCEPTED-AS-RULE — Fix A chosen, lesson "League organizer identity is
  single-sourced on OrganizerUserId, not on membership Role" appended to
  `context/foundation/lessons.md`. The user then also elected the code fix: `League.RowVersion`
  (`IsRowVersion()`) + migration `AddLeagueRowVersion`, `DbUpdateConcurrencyException` translated
  to `LeagueModifiedException` inside `TransferOrganizerAsync`, mapped to 409 in the controller so
  no EF Core type crosses the Api boundary. Known consequence: a transfer submitted twice against
  the same target from two tabs now returns 409 rather than succeeding idempotently.

### F2 — safeReturnTo misses control-character bypasses

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/client/src/auth/returnTo.ts:8-13`
- **Detail**: The filter rejects literal `//` and `/\` but not `/\t/evil.com` — browsers strip
  tab/CR/LF before parsing, so that string reads as `//evil.com`. Not exploitable through today's
  two call sites: the value only reaches `<Navigate>`, and `history.pushState` throws
  `SecurityError` rather than navigating cross-origin, so the realistic outcome is a broken
  redirect. But the guard does not do what its own comment claims, and the protection is
  incidental to another component — a future call site doing `window.location.href = destination`
  would reintroduce a real open redirect silently.
- **Fix**: Strip C0 control characters before the prefix checks (or resolve via
  `new URL(value, location.origin)` and compare `.origin`).
- **Decision**: FIXED — `safeReturnTo` filters C0 controls and DEL by char code before the prefix
  checks. Verified against 11 payloads including `/<tab>/evil.com`, `/<LF>//evil.com` and
  `/<CRLF>/evil.com`; all rejected, ordinary paths still allowed.

### F3 — CreateAsync retry mislabels unrelated InvalidOperationException

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs:226-233`
- **Detail**: The filter is `(collision) || (ex is InvalidOperationException)`, and the try block
  wraps both `nextCode(...)` and `SaveChangesAsync`. The `InvalidOperationException` arm exists for
  generator exhaustion, but it also swallows one thrown by `SaveChangesAsync` — reporting an
  unrelated EF Core failure as 503 "could not allocate an invite code" and hiding the real cause.
- **Fix**: Wrap only the `nextCode(...)` call in its own try/catch for `InvalidOperationException`;
  leave `SaveChangesAsync` guarded by the collision filter alone.
- **Decision**: FIXED — the two calls now sit in separate try blocks, each catching only the
  exception it can actually raise.

### F4 — Concurrent leave surfaces a raw 500 instead of idempotent success

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `LeagueRepository.cs:133-157`, `LeaguesController.cs:293-312`
- **Detail**: EF Core checks affected-row count on DELETE even with no concurrency token, so two
  racing `DELETE .../membership` calls give the loser a `DbUpdateConcurrencyException`. Nothing
  catches it and `Program.cs` has no global exception handler, so the caller gets a bare 500. The
  plan's own contract for `LeaveAsync` says "returning normally when no row exists keeps the
  endpoint idempotent" — the pre-check delivers that sequentially but not concurrently. The UI
  disables the button while busy, so this needs two tabs or a direct call.
- **Fix**: Catch `DbUpdateConcurrencyException` in `LeaveAsync` and return normally — the row is
  already gone, which is the requested end state, mirroring how `JoinAsync` treats its
  unique-violation.
- **Decision**: FIXED — `LeaveAsync` catches `DbUpdateConcurrencyException` and clears the change
  tracker, treating "already gone" as the requested end state.

### F5 — JoinLeaguePage skips the repo's problem-detail error pattern

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/client/src/routes/leagues/JoinLeaguePage.tsx:40-49`
- **Detail**: `ScoringCard`, `LeagueFormPage` and `MembersCard` all fall back to
  `err.problem?.detail ?? err.message`, because `api.ts:70` sets `ApiError.message` from
  `problem.title` first — so bare `.message` is usually "Not Found"/"Conflict", not the server's
  crafted text. `JoinLeaguePage` special-cases 404 (plan-sanctioned) but its fallback branch goes
  straight to `err.message`. Harmless while `/join` has only one `Problem()` path; wrong text the
  moment it gains another.
- **Fix**: Keep the 404 special case, change the fallback to `err.problem?.detail ?? err.message`.
- **Decision**: FIXED — 404 still gets its own copy; every other `ApiError` now falls back to
  `err.problem?.detail ?? err.message`, matching the sibling components.

### F6 — Roster read is unbounded

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/server/PredictionLeague.Infrastructure/Persistence/Repositories/LeagueRepository.cs:181-190`
- **Detail**: `ListMembersAsync` has no cap or pagination. The plan reasoned about this explicitly
  ("a handful of rows at friend-group scale") and deliberately left the list endpoint using
  `Memberships.Count` to avoid N+1. Noted only because it was scanned for; no action implied.
- **Decision**: NO ACTION — plan-sanctioned; not put to triage, since the plan's Performance
  Considerations already reasoned about roster size explicitly.

## Triage summary

- **Fixed**: F2, F3, F4, F5 (4)
- **Fixed + recorded as rule**: F1 (1) — lesson in `context/foundation/lessons.md`, plus the
  concurrency-token code fix on request
- **No action**: F6 (1, observation — plan-sanctioned)

Post-fix re-verification: `dotnet build` 0 errors, `ef migrations has-pending-model-changes` none,
no EF Core import in any controller, `npm run build` and `npm run lint` clean.

## Success criteria

Re-ran at review time, all green:

| Check | Result |
|---|---|
| `cd src/server && dotnet build` | 0 errors |
| `dotnet ef migrations has-pending-model-changes` | none |
| No EF Core import in any controller | confirmed |
| `cd src/client && npm run build` | clean |
| `cd src/client && npm run lint` | clean |

Pending: Progress row 3.10 (`npm run e2e`) was never executed — the suite needs the API (`:7182`)
and SPA (`:5173`) running and both were down at every probe. Manual rows 1.5-1.8, 2.2-2.10,
3.3-3.9 and 4.3-4.8 were confirmed by the user at the phase gates; no rubber-stamping flagged.
