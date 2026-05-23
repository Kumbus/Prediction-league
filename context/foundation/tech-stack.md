---
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
---

## Why this stack

Solo builder shipping a private, sports-agnostic match-prediction pool MVP in 3 after-hours weeks. Custom path: a split stack — ASP.NET Core webapi backend (`dotnet`, the verified recommended default for `(web, dotnet)`) plus a React SPA frontend (`vite-react`) for member screens. The .NET anchor clears all four agent-friendly gates with verified bootstrapper confidence, so backend scaffolding is smooth; the PRD's riskiest surface — the custom scoring engine — gains from C#'s explicit types and Entity Framework's typed data layer. OAuth sign-in (has_auth) fits ASP.NET Core Identity; the data-API ingest plus post-match recompute (has_background_jobs) suits a hosted service with scheduled work. React handles standings and prediction submission against the API. Note: the registry has no bundled .NET+React card — bootstrapper scaffolds the .NET anchor; the Vite React frontend is a manual follow-up, and vite-react is light on baked-in conventions (routing/data layer assembled by hand). Payments, realtime, and AI are out of scope per PRD. Deploys to azure-app-service; CI on GitHub Actions with manual promotion (staging gate before prod).
