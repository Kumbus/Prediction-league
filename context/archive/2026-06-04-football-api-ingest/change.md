---
change_id: football-api-ingest
title: Football data API client + scheduled ingest of fixtures/results
status: archived
created: 2026-06-04
updated: 2026-08-03
archived_at: 2026-08-03T17:04:13Z
---

## Notes

from @context/foundation/roadmap.md F-03

Roadmap F-03 (foundation): football data API client + scheduled ingest (Azure Functions timer) populating fixtures, results, and per-match detail (scorers, cards) into the F-01 data layer. PRD refs FR-004, FR-005, NFR (results within minutes). Prereq F-01 (done). Unlocks S-02, S-07.

**Blocker RESOLVED (2026-06-04):** source = **API-Football (api-sports.io), free tier** — €0 budget is the hard constraint. Direct key (`x-apisports-key`), not RapidAPI. Free tier: 100 req/day, 10 req/min, all endpoints incl. `Events` (goals + cards).

- OQ #1 (source) → resolved: API-Football.
- OQ #2 (granular fallback) → resolved: `Events` gives scorers + cards at €0, so **no final-score-only degrade** needed; S-02 keeps granular scoring.
- Constraint: 100 req/day → ingest must be poll-frugal (no live-15s polling) + cache hard. Upgrade path: Pro $19/mo (7,500/day) at launch, identical data shapes.

Full comparison + call-budget math: see `api-research.md` (sibling).
