---
change_id: walking-skeleton-deploy
title: Walking-skeleton Azure deploy — layered API + Azure SQL, first prod migration
status: implementing
created: 2026-06-07
updated: 2026-06-08

# Resource audit trail (post-Phase 2)

| Resource              | Name                                         | SKU / Plan                         | Region        |
| --------------------- | -------------------------------------------- | ---------------------------------- | ------------- |
| Resource group        | rg-prediction-league                         | —                                  | polandcentral |
| App Service plan      | asp-prediction-league (existing)             | F1 Linux                           | polandcentral |
| API Web App           | prediction-league-api-0523444a (existing)    | .NET 10                            | polandcentral |
| SQL server            | sql-prediction-league                        | logical server, sqladmin           | polandcentral |
| SQL database          | appdb                                        | Basic (DTU, 5)                     | polandcentral |
| SQL firewall rule     | AllowAzureServices (0.0.0.0/0.0.0.0)         | "Allow Azure services"             | —             |
| Storage account       | stpredictionleague                           | Standard_LRS, StorageV2            | polandcentral |
| Function App          | func-prediction-league                       | **Flex Consumption** (Y1 unavail.) | polandcentral |
| Function App runtime  | dotnet-isolated 10.0                         | —                                  | —             |
| Application Insights  | func-prediction-league (auto-created by CLI) | —                                  | polandcentral |
| Static Web App (SPA)  | (existing from 2026-05-23 deploy)            | —                                  | —             |

**Phase 2 runtime deviation**: classic Y1 Linux Consumption was unavailable in `polandcentral` (`Linux dynamic workers are not available in resource group`) — fell back to **Flex Consumption** per Phase 1 decision. Billing model still consumption.

**Secrets** (NOT committed):
- SQL admin password: surfaced once in chat to operator at Phase 2 manual gate; to be stored in operator's password manager and GitHub repo secrets in Phase 4.
- API-Football key: already held by operator (used in dev appsettings); to be set as App/Function app setting in Phase 3.
- Google OAuth client id/secret (prod): not yet created; to be set in Phase 3.
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
