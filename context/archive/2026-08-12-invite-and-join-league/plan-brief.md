# Invite and join a league — Plan Brief

> Full plan: `context/changes/invite-and-join-league/plan.md`

## What & Why

Roadmap slice S-05. A league's invite code exists and is displayed, but nothing consumes it — there
is no way for a second person to get into a league. This slice makes the code work: a friend joins
by typing it or following a link, the league shows who is actually in it, a member can leave, and an
organizer can hand the league over (FR-007, FR-002).

## Starting Point

`LeagueMembership` already exists with a unique index on `(LeagueId, UserId)` and shipped in the
initial migration; the organizer has been getting a membership row since S-03. `InviteCode` is
unique-indexed and generated over a dictation-friendly alphabet. What is missing is every write path
other than "create a league", a read *by* invite code, and any UI that names members. Separately,
`SignInPage` throws away `state.from`, so a deep link cannot survive sign-in — the invite link is
the first feature that needs it.

## Desired End State

A signed-in user who receives a code or a link ends up on the league page as a member, and that page
names everyone in the league. A member can leave in one click; an organizer transfers the league to
another member first, then leaves by the same path as anyone else.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Join surface | Code form + prefilling link (`/app/leagues/join/:code`) | One server path serves both, and a link is what people actually paste into a chat. |
| Roster visibility | Member list with display names, visible to all members | `memberCount` cannot tell an organizer whether the right friend joined; S-07's table needs the same data. |
| Leave | In scope, self-service | A mistaken join should not be permanent. |
| Organizer exit | Transfer first, then leave normally | Keeps `OrganizerUserId` always populated without a compound endpoint. |
| Last member out | Leaving deletes the league | Otherwise a solo organizer can never undo creating a league — transfer needs a target and there is no delete. |
| Transfer shape | Separate `PUT /organizer`, then ordinary leave | Two simple operations; transferring is useful on its own. |
| Repeat join | Idempotent 200, not 409 | A re-clicked chat link is a normal event, not a failure. |
| Late join | Allowed, unrestricted | The S-06 kickoff lock already prevents predicting played matches. |
| Code rotation | Out of scope | FR-007 covers inviting, not access revocation. |
| `JoinedUtc` | New column + migration | Join order is unrecoverable if not captured now; also orders the roster stably. |
| S-03 boundary debt | Paid off here | `lessons.md` names this slice; the retry moves behind `LeagueRepository.CreateAsync`. |

## Scope

**In scope:** join by code and by link; idempotent re-join; roster with display names; leave
(deleting the league when the leaver is its last member); organizer transfer; `JoinedUtc` column;
deep-link survival through both sign-in paths; moving the invite-code collision retry out of the
controller.

**Out of scope:** code rotation/revocation; email invites; organizer kicking a member; a standalone
delete endpoint or any way to remove a league other people are in; blocking late joins; predictions, standings, points; an FK from `LeagueMembership.UserId`
to `AspNetUsers`; tests (no suite exists).

## Architecture / Approach

Server: one additive column, four repository methods (`GetByInviteCodeAsync`, `JoinAsync`,
`LeaveAsync`, `TransferOrganizerAsync`) plus a roster projection that joins `AspNetUsers` inside
Infrastructure and returns a DTO. Three endpoints on the existing `LeaguesController`
(`POST /join`, `DELETE /{id}/membership`, `PUT /{id}/organizer`), all following the established
404-masks-invisible / 403-for-visible-but-forbidden convention. Client: a join page serving both
entry paths, a two-line fix so sign-in returns the user to where they were headed, and a
`MembersCard` mirroring how `ScoringCard` owns its slice of the detail page.

Two invariants hold the design together: `League.OrganizerUserId` and the `Role = Organizer`
membership row are always written in the same save, and an unknown invite code is indistinguishable
from a league you cannot see.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Server — data layer | `JoinedUtc` + migration, read-by-code, membership writes, roster projection, boundary-debt payoff | Refactoring the create path while adding to it — league creation must not regress |
| 2. Server — membership API | Join / leave / transfer endpoints, roster on the detail response | Transfer desynchronizing `OrganizerUserId` from the role rows |
| 3. Client — join flow | Join page, invite link, sign-in return-to | The Google round trip leaves the SPA; the return path must be re-derived from the URL |
| 4. Client — roster, leave, transfer | Members card with both membership actions | Post-transfer view must come from the server response, not local guessing |

**Prerequisites:** S-03 (shipped). Two accounts and a published tournament for manual verification.
**Estimated effort:** ~2 sessions across 4 phases; phases 1-2 are the bulk.

## Open Risks & Assumptions

- Join has a genuine race: two rapid submits both pass the membership pre-check, and the unique
  index catches the second. Handled by translating that specific violation into success — mis-scoped
  detection would swallow unrelated write failures.
- `JoinedUtc` for pre-existing rows is a migration default, not a real join time. Acceptable: only
  local rows exist.
- Assumes `DisplayName` is populated for every user (it is `required` on `ApplicationUser`); a
  membership whose user row is missing is skipped rather than rendered nameless.
- Transfer and leave together mean a league can end up with members but a stale organizer if a write
  is partial — mitigated by doing each as a single `SaveChangesAsync`.

## Success Criteria (Summary)

- A friend who receives a code or a link ends up in the league without help from the organizer.
- The league page names everyone in it, so the organizer can see the invite worked.
- A member can leave, and an organizer can hand the league over and then leave.
