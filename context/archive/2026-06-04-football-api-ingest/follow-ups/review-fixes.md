# Review follow-ups — football-api-ingest

Queued from impl-review (2026-06-07). See `reviews/impl-review.md`.

## F4 — Migration is destructive / reinterprets existing data (for F-04 release)

`20260607113246_AddFootballIngestModel` renames `Type→MatchEventTypeId` and
`HomeTeam→Round`, and adds non-null FK columns with `defaultValue: Guid.Empty`
before creating `Restrict` FKs. Safe only on an **empty** database.

**Action for F-04 (walking-skeleton-deploy):** confirm the target DB is empty
before applying this migration. Do not run it against any DB carrying existing
`Matches`/`MatchEvents` rows — the FK-creation step would fail or leave
`Guid.Empty` references to non-existent Teams/Players. Prod migrations stay
forward-only + human-gated.
