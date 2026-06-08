# Walking-skeleton Azure deploy (F-04) — Plan Brief

> Full plan: `context/changes/walking-skeleton-deploy/plan.md`

## What & Why

Provision the production backend pieces the first deploy deferred — **Azure SQL Basic**, a **Storage account**, and a **Consumption Function App** — and automate deploy via **GitHub Actions on merge to `main`**. The walking skeleton proves the deployed shape (`/health/db` reaching prod Azure SQL) end-to-end before S-01 ships, so later slices build on a real environment instead of an assumed one.

## Starting Point

API + SPA shells were deployed 2026-05-23 against bare-scaffold code, explicitly deferring the DB and Functions. F-01 (EF/persistence), F-02 (auth), F-03 (ingest + Functions host) have since landed, so the API now needs a real DB to boot meaningfully. Existing Azure resources (`rg-prediction-league`, F1 Linux plan, API app, SWA) are reused.

## Desired End State

Merging to `main` builds + deploys the API and Functions and applies the EF migration to Azure SQL. `/health/db` returns Healthy against prod, API endpoints respond, the ingest timer is registered on Consumption, and the SPA still serves over the CDN.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Deploy mechanism | GitHub Actions → prod on merge to `main` | Reproducible deploys, no local-machine dependency | Plan |
| Prod migration | **Auto-migrate on merge** | User choice for MVP — *deviates* from the documented human-gated guardrail | Plan |
| Migration safety | Green-build gate + idempotent script + PITR rollback | Forward-only migrations can't be undone by code revert | Plan |
| Functions | Deployed now (in scope) | Bundle ingest into the first real backend deploy | Plan |
| DB auth | SQL auth connection string (app setting) | Exactly what infra-v2 specifies; fastest | Plan |
| CI → Azure auth | Publish-profile secret | Fastest to wire for a single Free app; no AAD app needed | Plan |
| Staging | Deferred | Keep the skeleton thin; avoid second F1 app on shared quota | Plan |

## Scope

**In scope:** Azure SQL Basic, Storage account, Consumption Function App, app-settings/secrets wiring, GitHub Actions build+deploy+migrate pipeline, end-to-end `/health/db` verification, deviation documentation.

**Out of scope:** Staging app, OIDC CI auth, Key Vault, Managed Identity DB auth, Front Door/multi-region, App Insights, new app features.

## Architecture / Approach

Provision missing Azure resources (SQL Basic + Storage + Consumption Function App) in the existing Poland Central resource group → configure their app settings/secrets (one shared `ConnectionStrings__DefaultConnection`) → a `deploy-backend.yml` workflow builds/publishes API + Functions, deploys both via publish profiles, and applies an idempotent EF script behind a transient runner-IP firewall rule → smoke-verify the deployed shape.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Preflight & verification | Account/region/runtime confirmed | .NET 10 isolated not on Consumption in-region |
| 2. Provision infrastructure | SQL Basic + Storage + Function App | Region/SKU availability |
| 3. Configure settings & secrets | API + Function App wired to prod DB/keys | Secret leakage into repo |
| 4. CI/CD + auto-migrate | Deploy-on-merge pipeline w/ migration | Unattended bad migration hits prod |
| 5. Verify + record deviation | Green `/health/db`; docs aligned | — |

**Prerequisites:** F-01 (migrations) + F-02 (auth) landed ✅; human `az login`; Azure subscription.
**Estimated effort:** ~2–3 sessions across 5 phases (provisioning + pipeline authoring dominate).

## Open Risks & Assumptions

- **Auto-migrate on prod contradicts infra-v2 / roadmap F-04 / lessons** ("never auto-migrate prod"). Adopted consciously for the MVP; mitigated by green-build gate, idempotent script, and Azure SQL Basic PITR (~7-day) as the only rollback. Revisit before real users (S-01 GA).
- .NET 10 isolated on Consumption in Poland Central is an infra-v2 *unknown* — Phase 1 gates it, Flex Consumption is the fallback.
- F1 Free 60 CPU-min/day cap → 403 risk on match-day spikes (upgrade to B1 if it pinches).
- Publish-profile secret is long-lived (rotate on leak; OIDC is the deferred hardening).

## Success Criteria (Summary)

- Merge to `main` → green pipeline that deploys API + Functions and migrates prod.
- `GET /health/db` returns Healthy against prod Azure SQL.
- Ingest timer registered on Consumption; SPA still served; deviation recorded in foundation docs.
