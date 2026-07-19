# Database seed scripts

Out-of-band SQL that must run **once per database** (dev container, and every fresh deploy
target) after `dotnet ef database update`. These are not EF `HasData` seeds — they are kept as
idempotent SQL so the ~250-row reference set doesn't bloat the migration/snapshot.

## seed-nationalities.sql — REQUIRED

ISO 3166-1 alpha-3 nationalities into `dbo.Nationalities`. **The player CSV import depends on
this**: with an empty table, every CSV row fails as `Unknown NationalityCode` and no player can
be imported. Idempotent (`IF NOT EXISTS` per code) — safe to re-run.

### Local dev (Docker SQL container)

```bash
# from src/server/, with the predictionleague-sql container up
docker cp db/seed-nationalities.sql predictionleague-sql:/tmp/seed-nationalities.sql
docker exec predictionleague-sql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d PredictionLeague \
  -i /tmp/seed-nationalities.sql
```

### Deploy (Azure SQL)

Run once against the target database after the migration step, e.g. with `sqlcmd -S <server>
-d PredictionLeague -G -i db/seed-nationalities.sql` (Entra auth) or via the Azure Portal query
editor. Verify with `SELECT COUNT(*) FROM dbo.Nationalities;` (expect ~250).

> Deploy runbook: the seed step is manual and forward-only, matching the migration policy
> (additive, human-gated). See `context/changes/admin-seed-tournament/change.md`.
