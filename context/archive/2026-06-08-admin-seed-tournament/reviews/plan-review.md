<!-- PLAN-REVIEW-REPORT -->
# Plan Review: S-02 Admin Seed Tournament

- **Plan**: `context/changes/admin-seed-tournament/plan.md`
- **Mode**: Deep
- **Date**: 2026-06-08
- **Verdict**: REVISE → SOUND (after triage)
- **Findings**: 1 critical, 4 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | FAIL |

## Grounding
5/5 paths ✓, 4/4 symbols ✓, brief↔plan ✓

## Findings

### F1 — Phase 4 Include chain references nonexistent navs

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 4 #1 — Match repository extension
- **Detail**: Plan's `.Include(m=>m.HomeTeam/AwayTeam/Events).ThenInclude(Player/Team/MatchEventType)` references nav properties that do not exist on `Match` (`Match.cs:4-33`) or `MatchEvent` (`Match.cs:35-54`). `MatchConfiguration.cs:21-29` and `MatchEventConfiguration.cs:14-29` use `HasOne<T>()` without a nav selector. Code as written will not compile.
- **Fix A ⭐ Recommended**: Project to DTO with explicit joins
  - Strength: No Domain churn; query stays under Phase 4 scope.
  - Tradeoff: Hand-written join more verbose than Include chain.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Fix B**: Add nav properties to Match + MatchEvent
  - Strength: Lets Include chain stand; cleaner read code.
  - Tradeoff: Expands Phase 1 scope; risks reshape of F-03 ingest path.
  - Confidence: MEDIUM.
  - Blind spot: Whether ingest write path depends on absence of navs.
- **Decision**: Fixed via Fix A (DTO + explicit join projection).

### F2 — EnsureAdminClaimAsync call sites missing user lookup

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 #9 — EnsureAdminClaim seam
- **Detail**: Helper takes `ApplicationUser`. `Login` (`AuthController.cs:67-76`) uses `PasswordSignInAsync(email,password,…)` and `ExternalCallback`'s linked branch (`AuthController.cs:134-137`) uses `ExternalLoginSignInAsync(provider,key,…)` — neither returns the user. Plan must specify the lookup or admin promotion silently skips on those paths.
- **Fix**: Spell out the lookups in plan: `FindByEmailAsync(request.Email)` in `Login`; `FindByLoginAsync(info.LoginProvider, info.ProviderKey)` in the existing-link branch — null-checked before `EnsureAdminClaimAsync(user)`.
- **Decision**: FIXED.

### F3 — Tournament→Matches cascade already configured

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 #6 / #7, Critical Implementation Details, Migration Notes
- **Detail**: Plan claims the migration modifies the Tournament→Matches FK to `OnDelete.Cascade` and emits `DropForeignKey + AddForeignKey`. `TournamentConfiguration.cs:16-19` already configures Cascade; nothing will be emitted for this edge.
- **Fix**: Drop the cascade-edit phrasing in #6/#7 and Migration Notes; state cascade is already in place.
- **Decision**: FIXED.

### F4 — Progress↔Success Criteria not 1:1 in Phase 3 + Phase 5

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: `## Progress`
- **Detail**: Per progress-format spec each Success Criteria bullet maps to one `- [ ] N.M`. Phase 3 plan has 7 manual bullets, Progress had 5 (merged idempotency+tournamentId at 3.6 and both conflict cases at 3.7). Phase 5 plan has 8 manual bullets, Progress had 7. Tracking goes stale on merged items.
- **Fix**: Split Phase 3 progress into 3.3–3.10 and Phase 5 into 5.3–5.10, one per plan bullet.
- **Decision**: FIXED.

### F5 — Player list pagination returns no total count

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 #2 + #5 — Player list
- **Detail**: `GET /api/players?page=&pageSize=` returns `IReadOnlyList<Player>`. Phase 5 list page renders a paged table but cannot compute page count or "next/prev disabled" without a total.
- **Fix A ⭐ Recommended**: Envelope `{ items, total, page, pageSize }`
  - Strength: One round trip; idiomatic; trivial client wiring.
  - Tradeoff: `COUNT(*)` per call; cheap at MVP scale.
  - Confidence: HIGH.
  - Blind spot: Recount cost at >100k players — not MVP concern.
- **Fix B**: Cursor-based paging
  - Strength: No COUNT; cheap at scale.
  - Tradeoff: Page jump impossible; UI must be infinite-scroll.
  - Confidence: MEDIUM.
  - Blind spot: None significant.
- **Decision**: Fixed via Fix A (`PagedResult<T>` + `PagedPlayersResponse`).

### F6 — PUT /api/players partial-update semantics

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 #5 — PlayersController PUT
- **Detail**: Plan defines PUT with partial-update semantics (null fields don't overwrite). REST convention: PUT replaces, PATCH partial-updates.
- **Fix**: Rename verb to PATCH.
- **Decision**: FIXED (verb renamed to PATCH; Progress 3.4 + Manual Verification updated).

### F7 — Multipart size cap wrong attribute

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 #8 — Import endpoint
- **Detail**: Plan says "configure via `MaxRequestBodySize` attribute". `MaxRequestBodySize` is a Kestrel/IIS server option; the action-level attribute is `[RequestSizeLimit]` (and `[RequestFormLimits(MultipartBodyLengthLimit=…)]` for multipart).
- **Fix**: Replace with `[RequestSizeLimit]` + `[RequestFormLimits]`.
- **Decision**: FIXED.
