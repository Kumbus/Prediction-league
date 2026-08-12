# Custom Scoring Rules (S-04) — Plan Brief

> Full plan: `context/changes/custom-scoring-rules/plan.md`

## What & Why

An organizer can pick **which** match parameters score in their league and **how many points** each is worth, and change that config until the tournament starts. This is the product wedge — per-league custom scoring — and it is the last input S-07's scoring engine is missing.

## Starting Point

S-03 shipped scoring as write-once-at-create and **complete**: `POST /api/leagues` demands every `ScoringParameter` exactly once with points 0–1000, where `0` means "does not score". `ScoringRule` rows and the `(LeagueId, Parameter)` unique index already exist. There is no update path anywhere — `ILeagueRepository` has no writer beyond `CreateAsync`, and `GetWithDetailAsync` is `AsNoTracking`. `LeagueDetailPage.tsx` renders the rules read-only and says so in its header comment.

## Desired End State

The league detail page's Scoring card carries an organizer-only **Edit** mode: a checkbox and points input per parameter, submitting only the active ones. Saving replaces the config and the table re-renders from the server's response. Once any match in the tournament has kicked off, the Edit button disappears, a lock notice takes its place, and the API rejects the write with 409 regardless of the UI.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Edit lock | Freeze at the tournament's first kickoff | Reuses `Match.KickoffUtc` — the same instant FR-010 locks predictions — so S-06/S-07 inherit a fairness rule that already holds. |
| Rule shape | Organizer picks active parameters | A wall of zeros was the wrong UI; selecting parameters states intent directly. |
| "Off" representation | No row = not scored | No schema change, and it removes the second way to say "doesn't score". |
| Create parity | `POST` accepts a partial set too | One validator, one contract, one client fieldset — create and edit cannot drift. |
| Validation floor | ≥1 active rule, points 1–1000 | Makes "active but worth 0" unrepresentable, and a league that scores nothing impossible. |
| Existing leagues | No data migration | Nothing consumes the rows pre-S-06; a first edit collapses the old six-row shape naturally. |
| Edit UX | Inline on the detail page | No new route or dialog primitive; the read-only table stays the default view for members. |
| Tests | None — manual verification | Explicit call under `main_goal: speed`; roadmap OQ #3 stays open and lands with S-07's engine. |

## Scope

**In scope:** partial rule contract on `POST` + new `PUT /api/leagues/{id}/scoring-rules`; kickoff-derived lock surfaced as `isScoringLocked`; `ILeagueRepository.GetForUpdateAsync` + `ReplaceScoringRulesAsync`; `IMatchRepository.AnyKickedOffAsync`; a shared `ScoringRulesFieldset` used by the create form and the detail page's new edit mode.

**Out of scope:** the scoring engine (S-07), tests, any schema/enum/data migration, league rename/delete, invite/join (S-05), predictions (S-06), rule-change history, co-organizers.

## Architecture / Approach

Two phases, server then client — the same split S-03 shipped under. The lock is **derived, not stored**: scoring is locked iff any `Match` in the league's tournament has `KickoffUtc <= now`, so nothing needs migrating or keeping in sync, and a league created mid-tournament is locked from birth. All persistence behaviour stays behind `ILeagueRepository` per the lessons.md rule that ORM types must not reach a controller.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Server | Partial-set validator shared by `POST`/`PUT`, the organizer-only update endpoint with its 401/403/404/409 ladder, the tracked rule-set replace, and `isScoringLocked` on the detail response | The rule-set replace must reconcile **in place** — a delete-then-add of the same `(LeagueId, Parameter)` in one `SaveChanges` can trip the unique index (EF Core does not guarantee delete-before-insert ordering) |
| 2. Client | Shared toggle-aware scoring fieldset, create form submitting only active rules, inline edit mode + lock notice on the detail page | `LeagueDetailPage` gains a second render tree — needs a clean split to stay within the client's one-component-per-concern rule |

**Prerequisites:** S-03 shipped (done). API on its https profile + SPA dev server, both running, for manual verification. Admin match screens available to backdate a kickoff for the lock test.
**Estimated effort:** ~2 sessions, one per phase.

## Open Risks & Assumptions

- **The two phases must deploy together.** Dropping the completeness requirement changes a shipped endpoint: Phase 1's points floor rejects the three zero-point rules the current create form sends on every submit. A server deployed ahead of its client breaks league creation outright. The phase boundary is a verification gate, not a ship gate.
- **The lock is one-way and coarse.** An organizer who mis-set points and only notices after the first kickoff has no recourse — there is no override. The create form warns when the chosen tournament has already started, but a league created then is unconfigurable for good. Accepted: the alternative is retuning points against known results.
- **Roadmap OQ #3 stays open.** No harness exists for the rule config, so S-07 begins its correctness work on the riskiest surface in the product with nothing to build on.
- **Assumption:** no predictions exist yet (`Prediction` is outside `AppDbContext`), so an edit invalidates nothing today. If S-06 lands before this slice, the lock rule needs re-examining against submitted predictions.

## Success Criteria (Summary)

- An organizer can choose a subset of scoring parameters at create time and change that choice later, and the league detail view reflects it immediately and after a reload.
- Only the organizer can edit; members see the same read-only table with no edit affordance and are refused by the API.
- Once the tournament has started, the config is frozen in both the UI and the API.
