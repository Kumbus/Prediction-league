# User Sign-in (S-01) Implementation Plan

## Overview

Ship the SPA-side sign-in experience for the prediction app on top of the already-wired F-02 auth API. The user opens the marketing landing page, hits a "Sign in" CTA, signs in via Google or local email/password, and lands on a thin `/app` shell that proves the cookie session is live. No league/standings UI yet — those are S-03+; this slice exists to validate the cross-origin cookie round-trip and to set up the authenticated shell future slices fill in.

## Current State Analysis

- **Backend complete.** `AuthController` (`src/server/PredictionLeague.Api/Controllers/AuthController.cs:18`) exposes `POST /api/auth/register|login|logout`, `GET /api/auth/me` (`[Authorize]`), `GET /api/auth/login/google?returnUrl=`, and `GET /api/auth/external-callback`. Errors come back as ProblemDetails on register and 401 on bad login. The external callback redirects with `?error=<code>` on failure (`account_exists`, `no_email`, `external_login_failed`, `provisioning_failed`, `link_failed`).
- **Cookie posture.** `AddAuthenticationAndIdentity` configures the Identity cookie for cross-origin use. The cookie is `Secure;SameSite=None` — it will only flow over HTTPS. `Cors:AllowedOrigins` in `appsettings.json` pre-allows `https://localhost:5173`. The API runs on the **https profile** at `:7182` for sign-in to work locally.
- **SPA today.** `src/client/src/App.tsx` renders only the landing page sections (`components/landing/*`). No router, no API client, no auth state. `package.json` deps: React 19, Vite 8, Tailwind v4, shadcn primitives (button/card/badge only). Path alias `@/*` → `src/`. `dist/` is gitignored. **No test runner** — `playwright` is in devDependencies but unused.
- **Shadcn primitives present.** `src/client/src/components/ui/`: `button.tsx`, `card.tsx`, `badge.tsx`. `input.tsx`, `label.tsx`, and the shadcn `form.tsx` (the RHF wrapper) are **not** vendored yet — they have to be added.
- **Vite config.** `vite.config.ts` enables `@vitejs/plugin-react` + `@tailwindcss/vite` + `@` alias; no https, no env wiring. There is **no `.env`** in `src/client/`; the API base URL is implicit.
- **Lesson on record.** `context/foundation/lessons.md` lessons are server-side (EF max-length, App Service deploy quirks) — not load-bearing for this SPA slice.

## Desired End State

- Running `npm run dev` in `src/client/` boots Vite on **`https://localhost:5173`** (self-signed cert via `@vitejs/plugin-basic-ssl`). The API runs on the https launch profile (`:7182`).
- Visiting `https://localhost:5173/` shows the existing landing page; the Navbar CTA reads **"Sign in"** when anonymous and **"Open app"** when signed in.
- `/sign-in` renders a Google button and a tabbed email/password panel (Login | Register). Submitting valid local credentials establishes the cookie session and navigates to `/app`. Validation/credential errors render inline (Identity ProblemDetails for register, generic "invalid credentials" for 401 login).
- Clicking the Google button redirects to `/api/auth/login/google?returnUrl=https://localhost:5173/sign-in`; after consent the browser lands on `/sign-in`, which sees `useAuth().status === 'authenticated'` and forwards to `/app` with the session cookie set. Failure cases (`?error=account_exists`, etc.) leave the cookie unset and land on `/sign-in?error=<code>` with a friendly inline message.
- `/app` is a protected route (`RequireAuth`): anonymous → redirect to `/sign-in` (preserving intended destination); signed in → header shows `displayName` + Sign-out button, body says "Leagues arrive in S-03." Sign-out calls `POST /api/auth/logout`, clears in-memory user, sends user back to `/`.
- A thin Playwright script (`tests/e2e/auth.spec.ts`) drives register → me → logout → me-returns-401 against the local stack. Runs via `npm run e2e` (not CI-wired).

## Key Discoveries

- F-02 already covers everything backend-side; this slice is pure client wiring (`context/changes/auth-oauth-scaffold/plan.md:5`).
- Cross-site cookie requires SPA on HTTPS for `Secure;SameSite=None` to be set — confirmed in F-02 Critical Implementation Details (`context/changes/auth-oauth-scaffold/plan.md:44`).
- `AuthController.ExternalCallback` already validates `returnUrl` against `Cors:AllowedOrigins` (`AuthController.cs:172`) — the SPA can safely pass an absolute return URL; same allowed-origins value drives both CORS and redirect-allow-list.
- React Router v7 supports SPA/library mode via `createBrowserRouter` + `RouterProvider` (Context7 `/remix-run/react-router`); the auth-redirect pattern is `<RequireAuth>` wrapper using `<Navigate to="/sign-in" state={{ from: location }} replace />` — simpler than loader middleware for this slice.
- shadcn `Form` primitive (`form.tsx`) is the documented wrapper around React Hook Form's `FormProvider`; pairs with `@hookform/resolvers/zod`. Vendoring it requires `@radix-ui/react-label` and `@radix-ui/react-slot` (slot is already a dep).

## What We're NOT Doing

- No league UI, standings, or predictions — S-03/S-06/S-07.
- No password reset / email confirmation / "forgot password" — F-02 explicitly out of scope; no email infra exists.
- No second OAuth provider; no provider-coverage fallback UI.
- No global state library (Zustand / Redux). `AuthContext` is enough; `TanStack Query` is deferred to the first data-list slice.
- No Vite proxy. The SPA talks to the API directly at its absolute URL (`VITE_API_BASE_URL`) — mirrors prod posture.
- No CI hook for the Playwright script in this slice (no CI suite exists for the client yet); the script is for local regression only.
- No production deployment of the client (Static Web Apps wiring is deferred — the F-04 walking skeleton only deployed the API).
- No automated unit tests; verification stays manual + the single Playwright happy-path script.
- No change to backend code beyond, if needed, ensuring `Cors:AllowedOrigins` already contains `https://localhost:5173` (it does).

## Implementation Approach

Three thin phases that build the SPA up layer by layer without ever leaving a half-working surface:

1. **Foundation** — install deps, switch Vite to https, set up env + API base, vendor missing shadcn primitives, build `AuthContext` and `apiClient`, scaffold `createBrowserRouter` with placeholder routes and a `RequireAuth` wrapper.
2. **Local sign-in surface** — replace placeholders with real forms (login + register), wire to `/api/auth/*`, build the authenticated `/app` shell + sign-out, swap the Navbar CTA. Verifies the full local round-trip without touching Google.
3. **Google + error UX + Playwright** — Google button, `?error=` rendering on `/sign-in`, Playwright happy-path script. Google round-trip stays manual; Playwright covers local accounts.

## Critical Implementation Details

- **Vite https is load-bearing.** The browser drops the API's `Secure;SameSite=None` cookie unless the SPA itself is on https. Use `@vitejs/plugin-basic-ssl`; on first hit the browser shows a self-signed-cert warning (`thisisunsafe` or accept once). Documenting this in `src/client/AGENTS.md` is part of Phase 1.
- **Always `credentials: 'include'`.** Every API call from the SPA must opt into sending cookies. Centralise this in `apiClient` so no caller has to remember.
- **Don't preflight `me` into a redirect loop.** The first `AuthContext` mount calls `GET /api/auth/me`; a 401 is the **normal anonymous state**, not an error. `RequireAuth` only redirects to `/sign-in` once the probe resolves; while pending, render a neutral placeholder (no flash).
- **External callback error codes are a closed set.** The list in `AuthController.ExternalCallback` is `external_login_failed`, `no_email`, `account_exists`, `provisioning_failed`, `link_failed`. Map each to a friendly string in `/sign-in`; unknown codes show a generic "Sign-in failed."
- **`returnUrl` must be absolute, origin-allowed, and point at `/sign-in`.** Pass `https://localhost:5173/sign-in`, not `/app` or `/sign-in` (relative) — the API needs an absolute URL to match against `Cors:AllowedOrigins`. (`Url.IsLocalUrl` on the API side covers SAME-origin paths, which the SPA is not.) Targeting `/sign-in` (not `/app`) is load-bearing for error UX: `ExternalCallback` appends `?error=<code>` to the returnUrl on failure, and `<Navigate>` from `RequireAuth` does NOT carry the query string when bouncing an anonymous visitor back to `/sign-in`. With `/sign-in` as the target, failure renders the alert inline, and success is handled by `SignInPage`'s `authenticated` → `/app` redirect (Phase 2 §4).

## Phase 1: Foundation wiring

### Overview

Stand up deps, https dev profile, env, missing shadcn primitives, auth context, API client, and a router with placeholder routes + a `RequireAuth` wrapper. End-state: visiting `/app` while anonymous redirects to `/sign-in`; the auth probe to `/api/auth/me` runs and resolves; no real auth UI yet.

### Changes Required

#### 1. Install new client dependencies

**File**: `src/client/package.json`

**Intent**: Add the runtime libs and the Vite https plugin in one batch so subsequent steps can rely on them.

**Contract**: Add `react-router-dom@^7`, `react-hook-form@^7`, `zod@^3`, `@hookform/resolvers@^3`, `@radix-ui/react-label@^2`. Add `@vitejs/plugin-basic-ssl@^2` to `devDependencies`. Run `npm install`; commit the resulting `package-lock.json`.

#### 2. Vite https + env

**File**: `src/client/vite.config.ts`, `src/client/.env.development` (new), `src/client/src/vite-env.d.ts`

**Intent**: Serve the SPA over https in dev (so the cross-site cookie flows) and expose a typed `VITE_API_BASE_URL` constant.

**Contract**:
- `vite.config.ts`: add `import basicSsl from '@vitejs/plugin-basic-ssl'` and include it in the `plugins` array. Set `server.https: true` (the plugin supplies the cert) and `server.port: 5173`.
- `.env.development`: `VITE_API_BASE_URL=https://localhost:7182`. Do **not** commit any secrets — none here.
- `vite-env.d.ts`: extend `ImportMetaEnv` with `readonly VITE_API_BASE_URL: string`.

#### 3. Vendor missing shadcn primitives

**File**: `src/client/src/components/ui/input.tsx` (new), `src/client/src/components/ui/label.tsx` (new), `src/client/src/components/ui/form.tsx` (new), `src/client/src/components/ui/tabs.tsx` (new)

**Intent**: Bring in the form-building primitives in shadcn-canonical form. They are owned in-repo per `src/client/AGENTS.md`.

**Contract**: Copy from the shadcn registry (`npx shadcn@latest add input label form tabs`) — they bring their own peer imports (`@radix-ui/react-label`, `react-hook-form`). The `Form` component must export `Form`, `FormField`, `FormItem`, `FormLabel`, `FormControl`, `FormDescription`, `FormMessage`. No bespoke edits; this is a registry import.

#### 4. API client (centralised fetch)

**File**: `src/client/src/lib/api.ts` (new)

**Intent**: One place that knows the base URL and always sends cookies. Returns typed JSON or throws an `ApiError` carrying status + parsed ProblemDetails body.

**Contract**:
- Export `apiFetch<T>(path: string, init?: RequestInit): Promise<T>` that builds `${import.meta.env.VITE_API_BASE_URL}${path}`, sets `credentials: 'include'`, defaults `Content-Type: application/json` when `init.body` is present, and `JSON.stringify`s body when it's an object.
- Export `class ApiError extends Error` with `status: number` and `problem?: { title?: string; detail?: string; errors?: Record<string, string[]> }`.
- On 204 return `undefined as T`; on 4xx/5xx parse the body and throw `ApiError`.

#### 5. Auth context + provider

**File**: `src/client/src/auth/AuthContext.tsx` (new), `src/client/src/auth/types.ts` (new)

**Intent**: Single source of truth for "who's signed in" across the app; exposes the `me`-probe lifecycle and sign-out.

**Contract**:
- `types.ts`: `export interface AuthUser { id: string; email: string; displayName: string; isGlobalAdmin: boolean }`.
- `AuthContext.tsx`: `AuthProvider` calls `apiFetch<AuthUser>('/api/auth/me')` on mount; state is `{ status: 'loading' | 'anonymous' | 'authenticated', user: AuthUser | null }`; a 401 transitions to `anonymous`, any other error logs and falls back to `anonymous`. Expose `{ status, user, refresh(): Promise<void>, signOut(): Promise<void> }`. `signOut` calls `POST /api/auth/logout` then sets `anonymous`. Export `useAuth()` hook that throws if used outside the provider.

#### 6. Router scaffold + RequireAuth

**File**: `src/client/src/main.tsx`, `src/client/src/routes/index.tsx` (new), `src/client/src/routes/RequireAuth.tsx` (new), `src/client/src/routes/LandingPage.tsx` (new), `src/client/src/routes/SignInPage.tsx` (new placeholder), `src/client/src/routes/AppShell.tsx` (new placeholder)

**Intent**: Wrap the app in `BrowserRouter`-equivalent (`RouterProvider`) and `AuthProvider`, define the three routes, and provide a guard around `/app`. Real page contents come in Phase 2/3 — Phase 1 ships placeholders.

**Contract**:
- `routes/index.tsx`: export a `router = createBrowserRouter([...])` with three routes:
  - `/` → `LandingPage` (renders the existing landing sections from `App.tsx`).
  - `/sign-in` → `SignInPage` (placeholder: heading "Sign in" only).
  - `/app` → wrapped in `<RequireAuth>` → `AppShell` (placeholder: "App shell — Phase 2 fills this in").
- `RequireAuth.tsx`: reads `useAuth().status`; while `loading`, render `null`; while `anonymous`, render `<Navigate to="/sign-in" state={{ from: location }} replace />`; while `authenticated`, render `<Outlet />` (or children).
- `main.tsx`: wrap `<RouterProvider router={router} />` inside `<AuthProvider>`; remove the direct `<App />` mount.

#### 7. LandingPage extraction

**File**: `src/client/src/App.tsx`, `src/client/src/routes/LandingPage.tsx`

**Intent**: Decouple the landing composition from being the app root so it becomes a route.

**Contract**: Move the section-composition JSX out of `App.tsx` into `LandingPage.tsx`. Delete `App.tsx` (or shrink to a single re-export) — `main.tsx` no longer references it.

#### 8. AGENTS.md note

**File**: `src/client/AGENTS.md`

**Intent**: One short paragraph capturing the https dev requirement, the env var, and the router/auth conventions so future agents don't relitigate them.

**Contract**: Add a "## Auth" section noting: SPA dev runs on `https://localhost:5173` via basic-ssl; API base lives in `VITE_API_BASE_URL`; auth state via `AuthProvider`/`useAuth`; protected routes wrap children in `<RequireAuth>`.

### Success Criteria

#### Automated Verification

- Build passes: `npm run build` from `src/client/`
- Lint passes: `npm run lint`
- Dev server boots on https: `npm run dev` prints `Local: https://localhost:5173/`

#### Manual Verification

- Visiting `https://localhost:5173/` shows the existing landing page (one-time cert warning accepted)
- Visiting `https://localhost:5173/app` while anonymous redirects to `/sign-in`
- DevTools → Network shows a `GET https://localhost:7182/api/auth/me` request that returns 401 and the SPA does not loop or error

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual checks before Phase 2.

---

## Phase 2: Local sign-in surface

### Overview

Replace the Phase-1 placeholders with the real sign-in UI and authenticated shell. `/sign-in` gets a Login | Register tab pair backed by RHF + zod, talking to `/api/auth/login` and `/api/auth/register`. `/app` gets a header with the user's display name and a Sign-out button. The marketing Navbar CTA swaps to reflect auth state. Google integration deferred to Phase 3.

### Changes Required

#### 1. Zod schemas

**File**: `src/client/src/auth/schemas.ts` (new)

**Intent**: Two small schemas the forms validate against; keeps form components free of validation rules.

**Contract**: `loginSchema` = `{ email: z.string().email(), password: z.string().min(1) }`. `registerSchema` = `{ email: z.string().email(), password: z.string().min(8), displayName: z.string().min(1).max(256) }`. Export the inferred types (`LoginFormValues`, `RegisterFormValues`).

#### 2. Login form

**File**: `src/client/src/auth/LoginForm.tsx` (new)

**Intent**: Email/password form that establishes a session, refreshes auth state, and routes the user on to the original destination (`location.state.from` if present) or `/app`.

**Contract**: Functional component using `useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) })` wrapped in shadcn `<Form>`. Submit handler `POST /api/auth/login` via `apiFetch`; on success call `useAuth().refresh()` then `navigate(from ?? '/app', { replace: true })`. On `ApiError` with `status === 401` set a form-level error "Invalid email or password." For other statuses surface `problem.title` or a generic message.

#### 3. Register form

**File**: `src/client/src/auth/RegisterForm.tsx` (new)

**Intent**: Create-account form that auto-signs-in on success (the API already does `SignInAsync`).

**Contract**: Same RHF+zod pattern. Submit `POST /api/auth/register`; on success `refresh()` + navigate to `/app`. On `ApiError` with a populated `problem.errors`, map server validation codes (`DuplicateUserName`, `PasswordTooShort`, etc.) onto the matching form fields via `form.setError`. Fall back to a form-level error otherwise.

#### 4. SignInPage layout

**File**: `src/client/src/routes/SignInPage.tsx`

**Intent**: Compose the page: heading, shadcn `<Tabs>` with Login and Register panels, room for the Google button (Phase 3 fills it in). If the user is already authenticated, redirect to `/app` to avoid the awkward "sign-in form for someone signed in" state.

**Contract**: Read `useAuth().status`. While `'loading'`, render `null` (no flash — same posture as `RequireAuth`); this matters in particular when the Google success path bounces back through `/sign-in` before forwarding to `/app`. While `'authenticated'`, render `<Navigate to="/app" replace />`. While `'anonymous'`, render a centred card with a `<Tabs defaultValue="login">` containing `<LoginForm>` and `<RegisterForm>`. Reserve a `<div data-slot="external-providers" />` placeholder above the tabs that Phase 3 will populate.

#### 5. AppShell + sign-out

**File**: `src/client/src/routes/AppShell.tsx`, `src/client/src/auth/SignOutButton.tsx` (new)

**Intent**: Authenticated landing — proves the cookie session is live and gives a visible way out. Body explicitly signals "more arrives in S-03" so reviewers don't expect league UI.

**Contract**:
- `AppShell`: top bar with the wordmark and a right-side cluster `{displayName} · <SignOutButton />`. Body card: heading "You're signed in" + paragraph "League creation arrives in S-03."
- `SignOutButton`: shadcn `<Button variant="outline">` whose `onClick` calls `useAuth().signOut()` then `navigate('/', { replace: true })`.

#### 6. Navbar CTA swap

**File**: `src/client/src/components/landing/Navbar.tsx`

**Intent**: Make the landing surface auth-aware so signed-in users see a way back into the app.

**Contract**: Replace the static "Join League" anchor with a `useAuth()`-driven `<Button asChild>`: `status === 'authenticated'` → `<Link to="/app">Open app</Link>`; otherwise → `<Link to="/sign-in">Sign in</Link>`. Use `react-router-dom`'s `<Link>` (the Navbar is now inside the router).

### Success Criteria

#### Automated Verification

- Build passes: `npm run build`
- Lint passes: `npm run lint`

#### Manual Verification

- `/sign-in` → Register tab with a fresh email/password/displayName creates a user (verifiable via DB row in `AspNetUsers`) and navigates to `/app`; reload of `/app` stays signed in
- `/sign-in` → Login with bad password shows "Invalid email or password." inline; login with good password navigates to `/app`
- `/sign-in` → Register with weak (<8 char) password shows the inline error from the server
- `/app` → Sign out returns to `/`; visiting `/app` again redirects to `/sign-in`
- Navbar CTA on `/` reads "Open app" while signed in and "Sign in" while anonymous

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual checks before Phase 3.

---

## Phase 3: Google round-trip + error UX + Playwright smoke

### Overview

Light up the Google button on `/sign-in`, surface external-callback errors as inline messages, and ship a single Playwright happy-path script for local regression on the email/password flow.

### Changes Required

#### 1. Google sign-in button

**File**: `src/client/src/auth/GoogleSignInButton.tsx` (new), `src/client/src/routes/SignInPage.tsx`

**Intent**: Hand the browser off to the API's Google challenge, with an absolute `returnUrl` the API will accept.

**Contract**:
- `GoogleSignInButton`: a `<Button>` whose `onClick` does `window.location.assign(\`\${import.meta.env.VITE_API_BASE_URL}/api/auth/login/google?returnUrl=\${encodeURIComponent(window.location.origin + '/sign-in')}\`)`. Not a `<Link>` — this leaves the SPA. `/sign-in` is the returnUrl (not `/app`) so external-callback failures land back on `/sign-in?error=<code>` and the alert renders; on success, `SignInPage`'s authenticated branch redirects to `/app`.
- `SignInPage`: render `<GoogleSignInButton />` inside the `data-slot="external-providers"` placeholder; add a divider above the tabs ("or continue with email").

#### 2. External-error mapping on /sign-in

**File**: `src/client/src/auth/externalErrors.ts` (new), `src/client/src/routes/SignInPage.tsx`

**Intent**: Translate the closed set of `?error=` codes from `ExternalCallback` into human messages.

**Contract**:
- `externalErrors.ts`: `export const EXTERNAL_ERROR_MESSAGES: Record<string, string> = { external_login_failed: 'Google sign-in didn\\'t complete. Please try again.', no_email: 'Your Google account did not return an email address.', account_exists: 'An account with that email already exists. Sign in with email and password instead.', provisioning_failed: 'We could not create your account. Please try again.', link_failed: 'We could not link your Google account. Please try again.' }`. Export a helper `messageForExternalError(code: string | null): string | null` returning a fallback "Sign-in failed." for unknown codes.
- `SignInPage`: read `?error=` via `useSearchParams`; if present render a destructive-styled alert above the providers slot.

#### 3. Playwright happy-path smoke

**File**: `src/client/tests/e2e/auth.spec.ts` (new), `src/client/playwright.config.ts` (new), `src/client/package.json`

**Intent**: One thin script exercising the local cookie round-trip so regressions get caught without manual clicking.

**Contract**:
- Add `@playwright/test@^1` to `devDependencies` (the existing bare `playwright` dep is the library — the runner + `defineConfig` live in `@playwright/test`; the new dep pulls `playwright` transitively, so the explicit `playwright` entry can be dropped). Run `npm install`; commit the updated lockfile.
- `playwright.config.ts`: import `defineConfig` from `@playwright/test` and export `defineConfig({ testDir: './tests/e2e', use: { baseURL: 'https://localhost:5173', ignoreHTTPSErrors: true } })`. Do **not** auto-start the dev server / API — the script assumes they're running.
- `auth.spec.ts`: one test that generates a unique email, visits `/sign-in`, fills the Register tab, asserts navigation to `/app` and that the displayName is visible, clicks Sign out, asserts the landing page is shown, then verifies the cookie was cleared by hitting the API origin **directly** (not the SPA's `baseURL`). Use `page.evaluate(async () => (await fetch('https://localhost:7182/api/auth/me', { credentials: 'include' })).status)` and assert the result is 401. (A bare `fetch('/api/auth/me')` would resolve against `baseURL=https://localhost:5173` and hit Vite, not the API.) The API origin can be hardcoded here — Playwright is a local-only smoke and the dev URL is fixed.
- `package.json`: add script `"e2e": "playwright test"`. Document in `AGENTS.md` that the script needs both servers up locally.

#### 4. AGENTS.md update

**File**: `src/client/AGENTS.md`

**Intent**: Note the e2e script and how to run it; clarify Google credentials are not needed for Playwright.

**Contract**: One paragraph under the "## Auth" section added in Phase 1: how to run `npm run e2e`, that the script depends on the API on the https profile and the SPA dev server, and that Google sign-in is verified manually only.

### Success Criteria

#### Automated Verification

- Build passes: `npm run build`
- Lint passes: `npm run lint`
- Playwright script passes locally: `npm run e2e` (with the API + SPA running on the https profiles)

#### Manual Verification

- `/sign-in` → click Google → redirected to Google consent → after consent the browser lands on `/app` with the user shown in the header
- A second Google sign-in with the same account reuses the existing user (no duplicate `AspNetUsers` row)
- Visiting `/sign-in?error=account_exists` shows the friendly inline message
- Visiting `/sign-in?error=unknown_code` shows the generic "Sign-in failed." fallback

**Implementation Note**: After automated verification passes, pause for human confirmation of the Google manual round-trip and the error-UX checks.

---

## Testing Strategy

### Unit Tests

- None — no Vitest runner exists in the repo and standing one up is out of scope for this slice. Form validation correctness is covered indirectly by the Playwright happy-path.

### Integration Tests

- N/A.

### Manual Testing Steps

1. Start the API on the https profile: from `src/server/PredictionLeague.Api/`, `dotnet run --launch-profile https`. Confirm `https://localhost:7182/health/db` returns healthy.
2. Start the SPA: from `src/client/`, `npm run dev`. Visit `https://localhost:5173/`, accept the self-signed cert warning once.
3. Register a new user via `/sign-in` → Register tab. Confirm navigation to `/app` and a row in `AspNetUsers`.
4. Sign out via `/app`'s Sign-out button. Confirm return to `/` and that `/app` is now redirect-protected again.
5. Sign back in via `/sign-in` → Login tab. Confirm `/app` displays the displayName.
6. Test failure modes: bad password on Login, weak password on Register, duplicate email on Register.
7. Click the Google button; complete consent on a real Google account; confirm the user is provisioned (or linked) and lands on `/app`. Repeat to confirm no duplicate.
8. Manually visit `/sign-in?error=account_exists` and `/sign-in?error=foo` to confirm the alert rendering.
9. Run `npm run e2e` with both servers up; confirm the script passes.

## Performance Considerations

Negligible — one extra request (`GET /api/auth/me`) on initial app load. The auth probe is single-flight per `AuthProvider` mount; `refresh()` after sign-in is the only re-fetch. Bundle adds ~30KB gz for react-router-dom + react-hook-form + zod combined, acceptable for the slice.

## Migration Notes

No data migration. No backend code changes expected unless `Cors:AllowedOrigins` in `appsettings.json` is missing `https://localhost:5173` — verify before Phase 1 and add it if absent.

## References

- Roadmap item S-01: `context/foundation/roadmap.md:124`
- F-02 plan + brief: `context/changes/auth-oauth-scaffold/plan.md`, `plan-brief.md`
- F-02 API surface: `src/server/PredictionLeague.Api/Controllers/AuthController.cs:18`
- F-02 external-callback error codes: `src/server/PredictionLeague.Api/Controllers/AuthController.cs:125`
- F-02 CORS / cookie posture: `src/server/PredictionLeague.Infrastructure/DependencyInjection.cs` (`AddAuthenticationAndIdentity`)
- Client conventions: `src/client/AGENTS.md`
- React Router v7 SPA + protected routes: Context7 `/remix-run/react-router` (`createBrowserRouter`, `<Navigate>` redirect pattern)
- React Hook Form + zod: Context7 `/react-hook-form/react-hook-form` and `/react-hook-form/resolvers`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Foundation wiring

#### Automated

- [x] 1.1 Build passes (`npm run build`)
- [x] 1.2 Lint passes (`npm run lint`)
- [x] 1.3 Dev server boots on `https://localhost:5173`

#### Manual

- [x] 1.4 `https://localhost:5173/` shows the landing page (cert warning accepted once)
- [x] 1.5 `/app` while anonymous redirects to `/sign-in`
- [x] 1.6 DevTools shows `GET /api/auth/me` returning 401 with no loop or error

### Phase 2: Local sign-in surface

#### Automated

- [ ] 2.1 Build passes
- [ ] 2.2 Lint passes

#### Manual

- [ ] 2.3 Register a fresh user → navigates to `/app`; reload stays signed in
- [ ] 2.4 Login with bad password shows inline "Invalid email or password."
- [ ] 2.5 Register with weak password shows server validation inline
- [ ] 2.6 Sign out returns to `/`; `/app` is redirect-protected again
- [ ] 2.7 Navbar CTA reads "Open app" when signed in, "Sign in" when anonymous

### Phase 3: Google round-trip + error UX + Playwright smoke

#### Automated

- [ ] 3.1 Build passes
- [ ] 3.2 Lint passes
- [ ] 3.3 Playwright happy-path passes (`npm run e2e` with API + SPA running)

#### Manual

- [ ] 3.4 Google button → consent → lands on `/app` with displayName shown
- [ ] 3.5 Second Google sign-in reuses the existing user (no duplicate row)
- [ ] 3.6 `/sign-in?error=account_exists` shows the friendly inline message
- [ ] 3.7 `/sign-in?error=unknown_code` shows the generic fallback message
