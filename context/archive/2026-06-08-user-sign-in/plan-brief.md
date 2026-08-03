# User Sign-in (S-01) — Plan Brief

> Full plan: `context/changes/user-sign-in/plan.md`

## What & Why

Ship the SPA-side sign-in experience on top of the already-wired F-02 auth API (FR-001, US-01). The user opens the marketing landing page, signs in via Google or local email/password, and lands on a thin authenticated shell that proves the cross-site cookie round-trip works. This is the first slice the roadmap places after auth + walking-skeleton-deploy and it sets up the authenticated shell every later slice (organizer, member, predictions) fills in.

## Starting Point

`AuthController` exposes the full F-02 surface — register/login/logout/me and Google challenge/callback — and the Identity cookie is configured `Secure;SameSite=None` with CORS pre-allowing `https://localhost:5173`. The SPA today is a marketing landing page only: no router, no API client, no auth state; shadcn primitives are limited to button/card/badge; Vite runs on plain http.

## Desired End State

`https://localhost:5173/` shows the landing page; the Navbar CTA reads "Sign in" or "Open app" based on auth state. `/sign-in` offers a Google button and a Login | Register tab pair; both paths establish the Identity cookie and land the user on `/app`, a thin shell with displayName + Sign-out and a "leagues arrive in S-03" placeholder. The Google failure cases (`account_exists`, `no_email`, …) surface as inline messages on `/sign-in`. A thin Playwright script exercises register → me → logout locally.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Router | react-router-dom v7 (data router) | Mainstream, React-19-friendly, S-03+ will need protected routes anyway | Plan |
| Post-auth landing | Minimal `/app` shell | Establishes the authenticated shell S-03 fills in; proves the round-trip | Plan |
| Sign-in paths | Google **and** email/password (login + register) | Matches F-02 alt-sign-in user decision; covers Google-less members | Plan (user) |
| Forms | react-hook-form + zod via shadcn `Form` | Documented shadcn pattern; type-safe schemas; low re-renders | Plan |
| Dev cross-origin | SPA on https via `@vitejs/plugin-basic-ssl` | Mirrors prod cookie posture; `Secure;SameSite=None` only flows over https | Plan |
| Auth state | `AuthContext` + thin fetch wrapper | Single source of truth, no extra deps; TanStack Query deferred to data-list slices | Plan |
| Verify | Manual + thin Playwright happy-path | Playwright already in devDeps; catches local-account regressions cheaply | Plan |

## Scope

**In scope:**
- Vite https + `VITE_API_BASE_URL` env
- React Router v7 with `/`, `/sign-in`, `/app` + `RequireAuth` guard
- shadcn primitives: input, label, form, tabs
- AuthContext + `useAuth` + centralised `apiFetch` (always `credentials: 'include'`)
- `/sign-in` — Google button + Login | Register tabs, RHF+zod, ProblemDetails inline errors, `?error=` mapping
- `/app` — displayName + sign-out
- Navbar CTA swap based on auth state
- One Playwright happy-path script (local, not CI)

**Out of scope:**
- Leagues, standings, predictions (S-03+)
- Password reset / email confirmation / "forgot password" (no email infra)
- Second OAuth provider
- TanStack Query / Redux / Zustand
- Vite proxy
- SPA production deployment
- Vitest / component unit tests
- CI wiring for Playwright

## Architecture / Approach

```
main.tsx
 └ AuthProvider   (probes GET /api/auth/me on mount → loading | anonymous | authenticated)
    └ RouterProvider
       ├ /         LandingPage   (Navbar CTA reads useAuth → "Sign in" | "Open app")
       ├ /sign-in  SignInPage    (GoogleButton + <Tabs> Login | Register, RHF+zod → apiFetch)
       └ /app      RequireAuth → AppShell (displayName + SignOut)
```

`apiFetch` is the one place `credentials: 'include'` and `VITE_API_BASE_URL` are applied; every consumer goes through it. Cross-site cookies require HTTPS on both sides — `@vitejs/plugin-basic-ssl` gets the SPA there in dev.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Foundation wiring | Deps, Vite https, env, vendored primitives, AuthContext, apiFetch, router + RequireAuth (placeholders) | `me`-probe redirect loop if 401 isn't treated as the normal anonymous state |
| 2. Local sign-in surface | Real Login/Register forms, /app shell + sign-out, Navbar CTA swap | Mapping Identity validation error codes onto RHF field errors |
| 3. Google + error UX + Playwright | Google button with absolute returnUrl, `?error=` rendering, Playwright happy-path script | `returnUrl` must be absolute and origin-allowed; relative paths fail the API's origin check |

**Prerequisites:** F-02 done (it is). API runs on the https launch profile (`:7182`). Google OAuth client already registered (Phase 3 only).
**Estimated effort:** ~1–2 sessions across 3 phases.

## Open Risks & Assumptions

- Self-signed cert from `@vitejs/plugin-basic-ssl` requires a one-time browser warning — fine in dev, but documented in `AGENTS.md` so reviewers don't trip on it.
- `Cors:AllowedOrigins` already lists `https://localhost:5173`; a quick verify in Phase 1 catches any drift.
- Playwright runs against the local stack — both servers must be up; the script does not start them.

## Success Criteria (Summary)

- Anonymous user → `/sign-in` → email/password sign-in → cookie set → `/app` shows the user; sign-out clears the session.
- Anonymous user → `/sign-in` → Google → consent → cookie set → `/app` shows the user; second Google sign-in reuses the row.
- `?error=` codes from the external callback render as friendly inline messages on `/sign-in`.
