---
change_id: layered-backend-persistence
title: Layered backend + EF Core persistence (roadmap F-01)
status: archived
created: 2026-05-28
updated: 2026-08-03
archived_at: 2026-08-03T17:04:13Z
---

## Notes

From `context/foundation/roadmap.md` F-01.

Rebuild backend as layered solution (Domain / Application / Infrastructure) with EF Core wired to a database + initial migration covering entities the earliest slices need.

- PRD refs: FR-002 (per-(user, league) keying from the start), NFR (freshness — persisted standings recompute)
- Prerequisites: — (sequenced first; current `static List<League>` store is throwaway)
- Unlocks: S-02, S-03, every downstream data slice; F-03 ingest writes through this layer
- Scope guard: do NOT pre-build whole schema — layered skeleton + persistence wiring + only entities S-02/S-03 exercise; later slices add own migrations.

## Epilogue

- **Connection string config**: the dev `DefaultConnection` lives entirely in `dotnet user-secrets`, not in `appsettings.Development.json` (plan had described the key in that file). `appsettings*.json` carry no connection string — cleaner than planned, same no-committed-secret intent. Set via `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."` for the Api project.
