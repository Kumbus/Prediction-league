# Organizer creates a league (S-03) — Plan Brief

> Full plan: `context/changes/organizer-create-league/plan.md`

## What & Why

A signed-in user creates a private league bound to a published tournament, sets its scoring values,
and gets an invite code. This is the first slice where a real user owns something in the product —
it unlocks S-04 (edit scoring rules) and S-05 (invite friends), and every downstream slice reads
through the `LeagueMembership` rows this slice starts writing (FR-006, FR-008 partial, US-01).

## Starting Point

The schema landed in F-01: `League`, `LeagueMembership`, and `ScoringRule` entities all exist, with
a unique index on `InviteCode` and cascade deletes onto both child collections. Auth landed in F-02
(cookie Identity, global-admin claim policy, per-league roles deliberately left to
`LeagueMembership`). What's missing is the API — the pre-F-01 `static List<League>` controller was
deleted and never replaced — and any member-facing screen: `AppShell.tsx:31` still reads "League
creation arrives in S-03."

## Desired End State

From `/app`, a user opens "My leagues", creates a league against a published tournament with six
scoring values prefilled, and lands on a league page showing the invite code and rule table. The
league appears in their list and nowhere else — another signed-in user gets a 404 on it.

## Key Decisions Made

| Decision | Choice | Why |
| --- | --- | --- |
| Slice scope | Create + list + detail | Detail page is where S-04 (rules) and S-05 (invite) attach; create-only would just defer the same work |
| Scoring rules | Organizer sets all six in the create form | User's call — S-04 becomes edit-of-existing rather than first-fill |
| Which parameters | All six, prefilled | `ScoringParameter` is append-only, so iterate the enum; goal scorers and cards are the product wedge, not an afterthought |
| Invite code | Generated at create | Column is NOT NULL + unique today — create cannot succeed without one; S-05 inherits a usable code |
| Organizer membership | `LeagueMembership` row with role Organizer | FR-002 wants per-(user, league) keying from the start; the organizer also predicts, so they must be in the table |
| Tournament eligibility | Published only | Matches the existing visibility rule at `TournamentsController.cs:79`; drafts must not leak |
| Edit / delete league | Out of scope | Delete raises unanswered questions about memberships and predictions |

## Scope

**In scope:** `POST /api/leagues`, `GET /api/leagues`, `GET /api/leagues/{id}`; invite-code
generator; repository queries; three client screens; `AppShell` entry point.

**Out of scope:** join-by-code (S-05), scoring-rule editing (S-04), league rename/delete,
standings and predictions (S-06/S-07), a reusable league-scoped authorization policy, tests.

## Architecture / Approach

`LeaguesController` (first one in the repo) over `ILeagueRepository`, following the
`TournamentsController` conventions: nested request/response records, `Problem(...)` on validation
failure, `NotFound()` rather than `Forbid()` when hiding another user's league. Create is one
transactional unit — league + six scoring rules + organizer membership in a single
`SaveChangesAsync`. Client screens go under `routes/leagues/`, guarded by the existing
`RequireAuth`, fetching through `apiFetch`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Server — League API | Create/list/detail endpoints, invite-code generator, repo queries | Invite-code collisions: the unique index is the real guarantee, so the save path must handle a race, not just the pre-check |
| 2. Client — league screens | List, create form, detail page, `AppShell` entry | Form has six scoring inputs — generate them from the parameter list, don't hand-write six blocks |

**Prerequisites:** S-01 and S-02 shipped (both archived); local SQL container up; at least one
**published** tournament seeded, otherwise the tournament picker is empty.
**Estimated effort:** ~1-2 sessions across 2 phases.

## Open Risks & Assumptions

- No migration is expected — asserted via `dotnet ef migrations has-pending-model-changes` in Phase 1.
  If that reports drift, stop and reconcile before writing code.
- Scoring defaults (3 / 1 / 2 / 0 / 0 / 0) are a product guess, not a researched value set. They are
  prefill only — the organizer overrides them at create.
- Membership is checked inline in the controller. If S-05/S-06 need the same check, extract a
  policy then rather than pre-building one now.

## Success Criteria (Summary)

- A signed-in user creates a league from the browser and sees its invite code
- The league carries exactly six scoring rules and one organizer membership row
- Another user cannot see or open that league
