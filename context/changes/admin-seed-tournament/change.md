---
change_id: admin-seed-tournament
title: Admin seeds tournament with ingested match data
status: impl_reviewed
created: 2026-06-08
updated: 2026-07-19
archived_at: null
---

## Notes

S-02 from @context/foundation/roadmap.md

### Manual match entry (folded in)

Interim data source while paid API-Football ingest is deferred (see memory
manual-admin-match-entry). Admin adds matches by hand — both a form and a CSV import — instead
of a separate `admin-manual-matches` change.

- `Match.ExternalFixtureId` + `Team.ExternalTeamId` → nullable with **filtered** unique indexes
  (`WHERE ... IS NOT NULL`), so manual rows (NULL external id) coexist with ingested ones and
  ingest idempotency is preserved. Migration `AddManualMatchEntry` (additive, forward-only).
- `TeamsController` (list/create) — manual teams for the match pickers.
- Match write API on `TournamentsController`: `POST /api/tournaments/{id}/matches`,
  `GET/PUT/DELETE /api/matches/{matchId}`, `POST /api/tournaments/{id}/matches/import` (CSV,
  dry-run → commit; teams resolved by name, auto-created).
- `IMatchCsvImporter` / `CsvHelperMatchImporter` mirrors the player importer.
- Client: `MatchFormPage` (create/edit, inline team add), `MatchImportPage`, and Add/Import +
  per-match Edit/Delete on `TournamentDetailPage`.
- CSV headers: `HomeTeam,AwayTeam,KickoffUtc,Status,HomeScore,AwayScore,Round`. Manual matches
  carry no external key; re-uploading a file skips rows that duplicate an existing match
  `(home, away, kickoff)`.

### Required deploy step: nationality seed

`dbo.Nationalities` is **not** seeded by a migration — it's populated by an idempotent SQL
script run once per database. **The player CSV import breaks on an empty table** (every row →
`Unknown NationalityCode`). After `dotnet ef database update` on any fresh DB (dev container or a
deploy target), run `src/server/db/seed-nationalities.sql` — see `src/server/db/README.md`.
