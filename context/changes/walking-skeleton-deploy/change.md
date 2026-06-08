---
change_id: walking-skeleton-deploy
title: Walking-skeleton Azure deploy — layered API + Azure SQL, first prod migration
status: implementing
created: 2026-06-07
updated: 2026-06-08
archived_at: null
---

## Notes

from F-04 at @context/foundation/roadmap.md

### Phase 1 preflight (2026-06-07)

- Subscription: `dd204810-4214-41a4-880b-05fe99e649e4` (Visual Studio Enterprise – MPN), tenant `onedynamics.pl` — matches prior deploy. Confirmed via `az account show`.
- Region: `polandcentral` (reused from prior deploy).
- Resource group: `rg-prediction-league` (reused).
- Web runtime: `DOTNETCORE:10.0` listed by `az webapp list-runtimes --os-type linux`.
- Functions runtime: `dotnet-isolated` v10 confirmed available on **Flex Consumption** in `polandcentral` via `az functionapp list-flexconsumption-runtimes`. Listed in the global `list-runtimes` table for Linux; per-region SKU availability on classic Consumption (Y1) is not directly queryable.
  - **Decision**: attempt classic Consumption (Y1) create first per the plan's primary path; if creation in `polandcentral` rejects .NET 10 isolated, fall back to Flex Consumption (already verified). Either way the billing model stays consumption.
- Azure SQL Basic offered in `polandcentral` (`az sql db list-editions … --edition Basic` → `Available: True`).
- Providers `Microsoft.Sql` and `Microsoft.Storage` were `NotRegistered`; `az provider register` issued — registration in progress / completed before Phase 2.
