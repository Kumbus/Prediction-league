---
change_id: layered-backend-persistence
title: Layered backend + EF Core persistence (roadmap F-01)
status: implementing
created: 2026-05-28
updated: 2026-05-30
archived_at: null
---

## Notes

From `context/foundation/roadmap.md` F-01.

Rebuild backend as layered solution (Domain / Application / Infrastructure) with EF Core wired to a database + initial migration covering entities the earliest slices need.

- PRD refs: FR-002 (per-(user, league) keying from the start), NFR (freshness — persisted standings recompute)
- Prerequisites: — (sequenced first; current `static List<League>` store is throwaway)
- Unlocks: S-02, S-03, every downstream data slice; F-03 ingest writes through this layer
- Scope guard: do NOT pre-build whole schema — layered skeleton + persistence wiring + only entities S-02/S-03 exercise; later slices add own migrations.
