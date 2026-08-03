# OAuth Sign-in Scaffold (F-02) — Plan Brief

> Full plan: `context/changes/auth-oauth-scaffold/plan.md`

## What & Why

Wire authentication onto the F-01 Identity schema so accounts can be issued and requests authenticated (FR-001, Access Control). Two sign-in paths share one ASP.NET Core Identity cookie: Google external login and local email/password. This is the F-02 scaffold — it unblocks S-01 and every user-scoped slice (organizer, member, predictions).

## Starting Point

Identity schema is present but unwired: `AppDbContext` is an `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` and `InitialCreate` already created all `AspNet*` tables (incl. `AspNetUserLogins`). `Program.cs` calls `UseAuthorization()` with nothing registered — no schemes, no policies, no CORS, no Google package.

## Desired End State

Anonymous hits on protected routes return 401; email/password register→login→`me` works; admin-only endpoints return 403 to non-admins; `GET /api/auth/login/google` runs the full Google round-trip, provisioning/linking the user and issuing a session cookie, then redirecting to a validated SPA URL. No SPA UI is built (that's S-01).

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Auth scheme | Identity cookie | Identity-native, least code, no XSS-able token | Plan |
| OAuth flow | Server-side redirect (`AddGoogle`) | Built-in, secret stays server-side, pairs with cookie | Plan |
| Scope | Scaffold only (no SPA UI) | Matches roadmap; S-01 owns the UI | Plan |
| Provider | Google only | Roadmap default; one cred set | Plan |
| Alt sign-in | Email/password **added** | User wants coverage for Google-less members | Plan (user) |
| Role model | `IsGlobalAdmin` claim + admin policy; organizer/member per-league | Reuses F-01 model; org/member aren't global roles | Plan |
| Verify | Manual local round-trip + 401/403 | No test suite / no deploy yet; Google allows localhost | Plan |

## Scope

**In scope:** Identity + cookie auth pipeline; Google external login; local register/login/logout/`me`; admin authorization policy; CORS for the SPA; config + secrets wiring; `DisplayName` max-length lesson fix.

**Out of scope:** SPA sign-in UI (S-01); email confirmation / password reset; a second provider; Identity roles for organizer/member; prod deploy/CORS finalization (F-04); automated tests.

## Architecture / Approach

New Infrastructure DI extension `AddAuthenticationAndIdentity` (mirrors `AddFootballIngest`) registers `AddIdentity` (cookie schemes + `SignInManager`/`UserManager`), `AddGoogle`, a claims-principal factory emitting an admin claim from `IsGlobalAdmin`, the admin policy, and a credentialed CORS policy. `Program.cs` adds `UseCors` → `UseAuthentication` → `UseAuthorization` (order is load-bearing). A new `AuthController` carries the endpoints; `IngestController` gets the admin policy as the observable protected route.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Identity + cookie pipeline | Auth registered, CORS, admin policy, pipeline order, lesson fix | Cross-origin cookie config (`SameSite=None;Secure` needs HTTPS) |
| 2. Local accounts + endpoints | register/login/logout/`me`; protected ingest (401/403) | Identity error → ProblemDetails mapping |
| 3. Google external login + config | Challenge/callback, user provisioning/linking, secrets | First-login linking + open-redirect-safe `returnUrl` |

**Prerequisites:** F-01 (done). A Google Cloud OAuth client for Phase 3 manual verification (redirect URI `https://localhost:7182/signin-google`).
**Estimated effort:** ~1–2 sessions across 3 phases.

## Open Risks & Assumptions

- Cross-origin cookies require the **https** dev profile (`:7182`) to verify `Secure` cookies; over plain http the cookie won't set for cross-site.
- No schema migration expected — one is created only if the `DisplayName` max-length fix changes the column.
- Phase 3 live verification needs a real Google OAuth app; Phases 1–2 are fully verifiable without it.

## Success Criteria (Summary)

- Anonymous → 401, non-admin → 403, admin → through, on a real protected endpoint.
- Email/password and Google both establish a session and `GET /api/auth/me` returns the user.
- Google first-login provisions/links the user with no duplicate on repeat sign-in.
