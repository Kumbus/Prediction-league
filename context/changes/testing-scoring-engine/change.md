---
change_id: testing-scoring-engine
title: Testing scoring engine
status: implementing
created: 2026-08-31
updated: 2026-08-31
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

### Phase 1 deviation — test runner (2026-08-31)

The plan's *Critical Implementation Details* assumed `dotnet test` would default to VSTest
because the repo has no `global.json`. That is stale for this toolchain: xunit.v3 **4.0.0**
(the version the plan pins) ships on Microsoft.Testing.Platform, and the .NET 10 SDK removed
the VSTest target outright — `dotnet test` failed with
`Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later`.

Adapted, human-approved:

- Added repo-root `global.json` selecting `"runner": "Microsoft.Testing.Platform"`. No `sdk`
  key, so it pins no SDK version and CI's `setup-dotnet 10.0.x` is unaffected.
- Dropped `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` from the test project —
  both are VSTest-path packages with nothing to do under MTP. Final package set is
  `xunit.v3 4.0.0`, `Shouldly 4.3.0`, `NSubstitute 6.2.0`.

All other resolved versions match the plan's Key Discoveries exactly.
