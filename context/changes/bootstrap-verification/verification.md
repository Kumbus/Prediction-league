---
bootstrapped_at: 2026-05-23T12:24:00Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: prediction-league
language_family: multi
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "null"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: prediction-league
hints:
  language_family: multi
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: manual-promotion
  bootstrapper_confidence: verified
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: true
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: true
```

**Why this stack**: Solo builder shipping a private, sports-agnostic match-prediction pool MVP in 3 after-hours weeks. Custom path — split stack: ASP.NET Core webapi backend (`dotnet`, verified default for `(web, dotnet)`) plus a React SPA frontend (`vite-react`) for member screens. The .NET anchor clears all four agent-friendly gates; the custom scoring engine gains from C#'s explicit types and EF's typed data layer. OAuth fits ASP.NET Core Identity; data-API ingest + post-match recompute suit a hosted service with scheduled work. React handles standings and prediction submission against the API. Registry has no bundled .NET+React card — only the .NET anchor was scaffolded; the Vite React frontend is a manual follow-up.

## Pre-scaffold verification

| Signal      | Value   | Severity | Notes                                                                 |
| ----------- | ------- | -------- | --------------------------------------------------------------------- |
| npm package | not run | n/a      | non-JS starter; cmd_template invokes `dotnet new`, not a `create-*` CLI |
| GitHub repo | not run | n/a      | card.docs_url is learn.microsoft.com (not a github.com/<owner>/<repo>)  |

No remote recency signals available — the `dotnet new webapi` template ships with the installed .NET SDK (10.0.300), not a network-fetched package.

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 (prediction-league.csproj, prediction-league.http, appsettings.json, appsettings.Development.json, Program.cs, Properties/)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (dotnet new webapi emits none)
**.bootstrap-scaffold cleanup**: deleted

**Note — project rename**: `dotnet new -n` names the `.csproj`/`.http` after `{name}`, which the temp-dir strategy set to `.bootstrap-scaffold`. Because the .NET project filename is load-bearing (assembly + namespace), the two name-carrying files were renamed during move-up: `.bootstrap-scaffold.csproj` → `prediction-league.csproj` (RootNamespace `_bootstrap_scaffold` → `prediction_league`) and `.bootstrap-scaffold.http` → `prediction-league.http` (host-address variable renamed). Verified with `dotnet build`: succeeded, 0 warnings, 0 errors.

## Post-scaffold audit

**Tool**: skipped — no built-in audit tool for `multi`
**Recommended external tool**: for the .NET backend, run `dotnet list package --vulnerable` after restoring packages; for the React frontend (once added), run `npm audit`.

## Hints recorded but not acted on

| Hint                    | Value           |
| ----------------------- | --------------- |
| bootstrapper_confidence | verified        |
| quality_override        | false           |
| path_taken              | custom          |
| self_check_answers      | all 5 true      |
| team_size               | solo            |
| deployment_target       | azure-app-service |
| ci_provider             | github-actions  |
| ci_default_flow         | manual-promotion |
| has_auth                | true            |
| has_payments            | false           |
| has_realtime            | false           |
| has_ai                  | false           |
| has_background_jobs     | true            |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep. (None created this run.)
- Address audit findings per your project's risk tolerance — the full breakdown is in this log.
- Add the React frontend (`vite-react`) — not scaffolded by this run, since the hand-off was a multi-stack with the .NET webapi as the anchor.
