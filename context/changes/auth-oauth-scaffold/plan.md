# OAuth Sign-in Scaffold (F-02) Implementation Plan

## Overview

Wire authentication onto the existing F-01 Identity schema so accounts can be issued and requests authenticated. Two sign-in paths share one ASP.NET Core Identity **cookie**: Google external login (server-side redirect) and local email/password. Authorization is expressed through an **admin policy** backed by the existing `ApplicationUser.IsGlobalAdmin` flag; organizer/member stay per-league via `LeagueMembership` (untouched here). This is the scaffold the roadmap calls F-02 — it unblocks S-01 (sign-in slice) and every user-scoped slice, without building the user-facing sign-in UI (that is S-01).

## Current State Analysis

- **Identity schema present, unwired.** `AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` (`src/server/PredictionLeague.Infrastructure/Persistence/AppDbContext.cs:12`); `InitialCreate` migration already created all `AspNet*` tables incl. `AspNetUserLogins` (external-login store). No new migration is needed to add auth.
- **No auth configured.** `Program.cs:50` calls `app.UseAuthorization()` but nothing registers Identity, authentication schemes, or policies. There is no `UseAuthentication()` and no CORS.
- **`ApplicationUser`** (`src/server/PredictionLeague.Infrastructure/Identity/ApplicationUser.cs:8`) has `DisplayName` + `IsGlobalAdmin`; its comment already reserves "OAuth subject lives in AspNetUserLogins (F-02)".
- **Packages:** `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.8 is referenced in Infrastructure. The Google provider package is **not** present.
- **Conventions to mirror:** options-class-per-integration bound to a config section (`ApiFootballOptions` + `AddFootballIngest` in `DependencyInjection.cs`); secrets via user-secrets in dev (`UserSecretsId` set on the Api csproj); controllers `[ApiController] + [Route("api/[controller]")]` (`IngestController.cs`).
- **Split stack:** React SPA (Vite, dev `:5173`) and API (`:5185` http / `:7182` https) are different origins — cookie auth must be configured for cross-origin use (CORS `AllowCredentials`, cookie `SameSite`/`Secure`).
- **Lesson on record (already satisfied):** custom Identity string props must have explicit `HasMaxLength`. `DisplayName` already complies — `ApplicationUserConfiguration.cs:11` has `.IsRequired().HasMaxLength(256)` and `InitialCreate` already emits `nvarchar(256)` (snapshot matches). No drift exists; this change only re-verifies, it does not fix.

## Desired End State

Running the API locally:
- An anonymous request to a protected endpoint returns **401** (not a login-page redirect — .NET 10 cookie auth returns status codes for API endpoints).
- `POST /api/auth/register` then `POST /api/auth/login` with email/password establishes a session; `GET /api/auth/me` returns the signed-in user's id, email, displayName, isGlobalAdmin.
- A non-admin calling an admin-only endpoint gets **403**; an admin gets through.
- `GET /api/auth/login/google` redirects to Google; after consent the user is created/linked in `AspNetUsers`/`AspNetUserLogins` and a session cookie is issued, then the browser is redirected back to a configured SPA return URL.
- Google client id/secret and SPA origins come from configuration (user-secrets in dev), never committed.

## What We're NOT Doing

- No SPA sign-in UI / button / landing screen — that is **S-01**. F-02 ships only the API + endpoints.
- No email confirmation, password reset, or "forgot password" flows (no email infrastructure yet). Local accounts are register/login/logout only.
- No second OAuth provider (Google only).
- No role-management UI — `IsGlobalAdmin` is set directly in the DB for v1.
- No deployment/CORS-for-prod-origin finalization — F-04 owns the deployed shape; we wire config keys but verify locally.
- No ASP.NET Identity roles for organizer/member — those stay per-league via `LeagueMembership` (FR-002 keying), untouched.
- No automated test project (none exists in the repo; verification is manual + build).

## Implementation Approach

Add an Infrastructure DI extension (`AddAuthenticationAndIdentity`) mirroring the existing `AddInfrastructure`/`AddFootballIngest` pattern, registering Identity (cookie schemes + `SignInManager`/`UserManager`), the Google handler, an admin authorization policy, and a claims-principal factory that emits an admin claim from `IsGlobalAdmin`. Program.cs gains `UseCors` + `UseAuthentication` in the correct pipeline order. A new `AuthController` exposes the password endpoints, `me`, and the Google challenge/callback. The deliberately-thin verification surface is the existing `IngestController`, which gets the admin policy applied so 401/403 are observable.

## Critical Implementation Details

- **Pipeline order.** `UseCors` must precede `UseAuthentication`, which must precede `UseAuthorization`, which precedes `MapControllers`. The current `Program.cs` only has `UseAuthorization` — inserting the other two in order is load-bearing; wrong order silently breaks auth or CORS-with-credentials.
- **.NET 10 API cookie behavior.** In .NET 10 the Identity application cookie returns **401/403 for API endpoints instead of redirecting** to a login path — this is the desired headless behavior; do not add custom `OnRedirectToLogin` overrides to "fix" a redirect that won't happen.
- **Cross-origin cookie.** SPA and API are different origins. For the cookie to flow, CORS must allow the SPA origin **with credentials** (cannot use `AllowAnyOrigin` together with `AllowCredentials`), and the cookie needs `SameSite=None; Secure` for true cross-site use — which requires HTTPS. For local verification use the **https launch profile** (`:7182`) so `Secure` cookies work; document this in the verification steps.
- **`AddIdentity`, not `AddDefaultIdentity`.** `AddDefaultIdentity` pulls in the Razor Identity UI this API doesn't want. Use `AddIdentity<ApplicationUser, IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders()`; the Google handler chains on `AddAuthentication().AddGoogle(...)` and uses the Identity external cookie scheme by default.

## Phase 1: Identity + cookie auth pipeline

### Overview

Register Identity, cookie auth, the Google handler shell, CORS, and the admin authorization policy via a new Infrastructure DI extension; wire the pipeline in Program.cs. No endpoints yet — this phase proves the pipeline stands up and rejects anonymous access.

### Changes Required:

#### 1. Google provider package

**File**: `src/server/PredictionLeague.Infrastructure/PredictionLeague.Infrastructure.csproj`

**Intent**: Add the Google authentication provider so `AddGoogle` is available.

**Contract**: `PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.8"` (match the pinned 10.0.8 line used by the other ASP.NET packages).

#### 2. Options class for Google credentials

**File**: `src/server/PredictionLeague.Infrastructure/Identity/GoogleAuthOptions.cs` (new)

**Intent**: Bind `Authentication:Google` config to a typed options object, mirroring `ApiFootballOptions`.

**Contract**: class with `const string SectionName = "Authentication:Google"`, `string ClientId`, `string ClientSecret`.

#### 3. Authorization policy + admin claim factory

**File**: `src/server/PredictionLeague.Infrastructure/Identity/AuthorizationPolicies.cs` (new), `src/server/PredictionLeague.Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs` (new)

**Intent**: Express "global admin" as an authorization policy without inventing an Identity role. A claims-principal factory emits an admin claim when `ApplicationUser.IsGlobalAdmin` is true; a named policy requires that claim.

**Contract**: `AuthorizationPolicies.AdminOnly` constant (policy name) + a stable claim type constant (e.g. `"prediction:admin"`). `AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>` overriding `GenerateClaimsAsync` to add the admin claim when the flag is set. Policy registered as `RequireClaim(adminClaimType)`.

#### 4. DI extension wiring auth

**File**: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs`

**Intent**: One `AddAuthenticationAndIdentity(config)` extension the host calls, mirroring the existing extensions. Registers Identity with cookie schemes, the Google handler, the claims factory, and authorization policies.

**Contract**: new public static `IServiceCollection AddAuthenticationAndIdentity(this IServiceCollection, IConfiguration)`:
- `services.Configure<GoogleAuthOptions>(...)`.
- `AddIdentity<ApplicationUser, IdentityRole<Guid>>(...).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders()`.
- Register `AppUserClaimsPrincipalFactory` as the `IUserClaimsPrincipalFactory<ApplicationUser>`.
- `AddAuthentication().AddGoogle(o => { o.ClientId/ClientSecret from options; o.SaveTokens = true; })`.
- Configure the application cookie: `Secure`, `SameSite` (per Critical Details), reasonable expiry.
- `AddAuthorization(o => o.AddPolicy(AdminOnly, ...))`.

(CORS is a host/pipeline concern and is registered in `Program.cs` — see #6 — not in this Infrastructure extension.)

#### 5. `ApplicationUser` Fluent config — lesson compliance

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs`

**Intent**: Verify-only. `DisplayName` already maps `nvarchar(256)` (`ApplicationUserConfiguration.cs:11`); confirm it still does per the recorded lesson. No edit expected.

**Contract**: confirm `builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(256)` is present and the model has no pending drift. No change to this file is expected; if (unexpectedly) drift appears, see #7.

#### 6. Program.cs pipeline

**File**: `src/server/PredictionLeague.Api/Program.cs`

**Intent**: Register the CORS policy (host concern), call the new DI extension, and insert CORS + authentication into the request pipeline in the correct order.

**Contract**: register the CORS policy here — `builder.Services.AddCors(o => o.AddPolicy(<name>, p => p.WithOrigins(config "Cors:AllowedOrigins").AllowCredentials().AllowAnyHeader().AllowAnyMethod()))`; `builder.Services.AddAuthenticationAndIdentity(builder.Configuration)`; in the pipeline add `app.UseCors(<policy>)` then `app.UseAuthentication()` before the existing `app.UseAuthorization()`.

#### 7. Migration (not expected)

**File**: `src/server/PredictionLeague.Infrastructure/Persistence/Migrations/` (only in the unexpected event #5 surfaces drift)

**Intent**: Adding auth needs no migration (tables exist). `DisplayName` already maps `nvarchar(256)`, so no length migration is expected either.

**Contract**: run `dotnet ef migrations has-pending-model-changes` as the gate (success criterion 1.2). Expected result: clean. Only if it unexpectedly reports drift, `dotnet ef migrations add AdjustDisplayNameLength`.

#### 8. Config keys

**File**: `src/server/PredictionLeague.Api/appsettings.json`

**Intent**: Declare the config shape (empty values; real secrets via user-secrets).

**Contract**: `"Authentication": { "Google": { "ClientId": "", "ClientSecret": "" } }` and `"Cors": { "AllowedOrigins": [ "https://localhost:5173" ] }`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build src/server/prediction-league.slnx`
- No unintended pending model changes: `dotnet ef migrations has-pending-model-changes --project src/server/PredictionLeague.Infrastructure --startup-project src/server/PredictionLeague.Api` (clean, or a deliberate `DisplayName` migration committed)
- App starts and `GET /health/db` still returns healthy

#### Manual Verification:

- An anonymous request to a route marked `[Authorize]` (use a temporary marker or the Phase 2 endpoint) returns 401, not an HTML redirect
- App boots with empty Google config without throwing (handler registers lazily)

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual checks before Phase 2.

---

## Phase 2: Local accounts + auth endpoints

### Overview

Add `AuthController` with email/password register, login, logout, and `me`, all via Identity's `SignInManager`/`UserManager`. Apply the admin policy to the existing `IngestController` as the observable protected endpoint. This phase is fully verifiable without any Google credentials.

### Changes Required:

#### 1. Auth controller — local + me

**File**: `src/server/PredictionLeague.Api/Controllers/AuthController.cs` (new)

**Intent**: Expose the credential-free auth surface so a session can be established and inspected.

**Contract**: `[ApiController] [Route("api/[controller]")]`, constructor-injected `UserManager<ApplicationUser>` + `SignInManager<ApplicationUser>`:
- `POST register` `{ email, password, displayName }` → `UserManager.CreateAsync`; on success `SignInManager.SignInAsync`; returns 200 or a validation `ProblemDetails` from Identity errors.
- `POST login` `{ email, password }` → `SignInManager.PasswordSignInAsync`; 200 on success, 401 on failure.
- `POST logout` → `SignInManager.SignOutAsync`; 204.
- `GET me` `[Authorize]` → current user's `{ id, email, displayName, isGlobalAdmin }`; 401 when anonymous.

#### 2. Protect the verification endpoint

**File**: `src/server/PredictionLeague.Api/Controllers/IngestController.cs`

**Intent**: Use the real authorization policy as the demonstrable protected endpoint (replaces relying solely on the dev-only 404 gate).

**Contract**: add `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]`. Keep the existing `IsDevelopment()` 404 guard as defense-in-depth.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification:

- `POST /api/auth/register` creates a row in `AspNetUsers` (verify via DB or a follow-up `me`)
- `POST /api/auth/login` then `GET /api/auth/me` returns the user; `me` returns 401 before login (use the https profile so the auth cookie is set)
- `POST /api/auth/logout` clears the session — subsequent `me` returns 401
- A logged-in **non-admin** calling `POST /api/ingest/...` returns 403; flipping the user's `IsGlobalAdmin` to true in the DB and re-logging-in lets the request through

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual checks before Phase 3.

---

## Phase 3: Google external login + config

### Overview

Add the Google challenge/callback endpoints and first-login user provisioning, then complete configuration and a real end-to-end Google round-trip on localhost.

### Changes Required:

#### 1. Google challenge + callback endpoints

**File**: `src/server/PredictionLeague.Api/Controllers/AuthController.cs`

**Intent**: Start the server-side redirect flow and complete it by creating/linking the local user, then return the browser to the SPA.

**Contract**:
- `GET login/google?returnUrl=` → `Challenge` with `GoogleDefaults.AuthenticationScheme` and a `RedirectUri` pointing at the callback (carrying `returnUrl`).
- `GET external-callback?returnUrl=` → `SignInManager.GetExternalLoginInfoAsync()`; if a user with that login exists, `ExternalLoginSignInAsync`; otherwise create an `ApplicationUser` (email + `DisplayName` from Google claims), `AddLoginAsync`, then sign in. On success redirect to a validated SPA `returnUrl`; on failure return a problem/redirect to an error URL.

**Contract note (snippet — non-obvious linking step):**
```csharp
var info = await _signInManager.GetExternalLoginInfoAsync();
// info.LoginProvider == "Google", info.ProviderKey == Google subject ("sub")
// Map info.Principal claims -> ApplicationUser.Email / DisplayName on first login,
// then await _userManager.AddLoginAsync(user, info) to persist the AspNetUserLogins row.
```

#### 2. Return-URL safety

**File**: `src/server/PredictionLeague.Api/Controllers/AuthController.cs`

**Intent**: Prevent open-redirect — only redirect back to configured SPA origins.

**Contract**: validate `returnUrl` against `Cors:AllowedOrigins` (or `Url.IsLocalUrl` for same-origin); fall back to a default app URL otherwise.

#### 3. Dev secrets + config documentation

**File**: `src/server/PredictionLeague.Api/appsettings.json` (keys already added in Phase 1); secrets via user-secrets; brief note in `src/server/AGENTS.md`

**Intent**: Record how to supply Google credentials and register the OAuth app, without committing secrets.

**Contract**: `dotnet user-secrets set "Authentication:Google:ClientId" ...` / `ClientSecret`. Google Cloud console: OAuth client (Web), authorized redirect URI `https://localhost:7182/signin-google`. One short AGENTS.md note that auth is now wired (Google + local) and how to set secrets.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build src/server/prediction-league.slnx`

#### Manual Verification:

- Visiting `GET /api/auth/login/google` (https profile) redirects to Google's consent screen
- Completing consent creates rows in `AspNetUsers` + `AspNetUserLogins`, issues the session cookie, and redirects back to the configured SPA return URL
- A second Google sign-in with the same account reuses the existing user (no duplicate row)
- `GET /api/auth/me` after Google sign-in returns the provisioned user
- An invalid/foreign `returnUrl` is rejected (falls back to default, no open redirect)

**Implementation Note**: After automated verification passes, pause for human confirmation of the full round-trip.

---

## Testing Strategy

### Unit Tests:

- None — no test project exists in the repo and standing one up is out of scope (see "What We're NOT Doing"). Verification is build + manual.

### Integration Tests:

- N/A for this change.

### Manual Testing Steps:

1. Run the API on the **https** profile (`dotnet run --launch-profile https` in `src/server/PredictionLeague.Api`) so `Secure` cookies are set.
2. `POST /api/auth/register` (use `PredictionLeague.http`), then `POST /api/auth/login`; confirm `GET /api/auth/me` returns the user and returns 401 before login / after logout.
3. With a non-admin user, call `POST /api/ingest/{tournamentId}` → expect 403; set `IsGlobalAdmin = 1` in the DB, re-login, retry → expect the normal ingest response.
4. Hit `GET /api/auth/login/google`, complete consent, confirm redirect back + `me` returns the Google-provisioned user; repeat to confirm no duplicate user.

## Performance Considerations

Negligible — auth middleware on a friend-group-scale app (`target_scale`: low qps). Cookie auth avoids per-request token verification round-trips.

## Migration Notes

Adding auth requires **no schema migration** — the `AspNet*` tables (including `AspNetUserLogins`) already exist from `InitialCreate`. A migration is created **only** if the `DisplayName` max-length fix (Phase 1 #5) changes the column. Dev auto-migrates on startup; prod stays forward-only + human-gated (unchanged by this plan).

## References

- Roadmap item F-02: `context/foundation/roadmap.md:81`
- PRD FR-001 / Access Control: `context/foundation/prd.md:53`, `:98`
- Identity schema (F-01): `src/server/PredictionLeague.Infrastructure/Persistence/AppDbContext.cs:12`, `src/server/PredictionLeague.Infrastructure/Identity/ApplicationUser.cs:8`
- DI pattern to mirror: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs:43` (`AddFootballIngest`)
- Pipeline to extend: `src/server/PredictionLeague.Api/Program.cs:50`
- Lesson (Identity string max-length): `context/foundation/lessons.md`
- .NET 10 cookie API behavior + Google setup confirmed via Context7 (`/dotnet/aspnetcore.docs`, security/authentication)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Identity + cookie auth pipeline

#### Automated

- [x] 1.1 Solution builds (`dotnet build prediction-league.slnx`)
- [x] 1.2 No unintended pending model changes (or deliberate DisplayName migration committed)
- [x] 1.3 App starts and `GET /health/db` returns healthy

#### Manual

- [x] 1.4 Anonymous request to a protected route returns 401 (not an HTML redirect)
- [x] 1.5 App boots with empty Google config without throwing

### Phase 2: Local accounts + auth endpoints

#### Automated

- [ ] 2.1 Solution builds

#### Manual

- [ ] 2.2 `register` creates an `AspNetUsers` row
- [ ] 2.3 `login` then `me` returns the user; `me` returns 401 before login
- [ ] 2.4 `logout` clears session — subsequent `me` returns 401
- [ ] 2.5 Non-admin gets 403 on the ingest endpoint; admin (flag flipped) gets through

### Phase 3: Google external login + config

#### Automated

- [ ] 3.1 Solution builds

#### Manual

- [ ] 3.2 `login/google` redirects to Google consent
- [ ] 3.3 Consent creates `AspNetUsers` + `AspNetUserLogins` rows, issues cookie, redirects to SPA return URL
- [ ] 3.4 Second sign-in reuses the existing user (no duplicate)
- [ ] 3.5 `me` after Google sign-in returns the provisioned user
- [ ] 3.6 Invalid/foreign `returnUrl` is rejected (no open redirect)
