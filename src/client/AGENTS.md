# Client Guidelines

React 19 + Vite SPA (TypeScript) for Prediction League member screens — standings and prediction submission. Self-contained; consumes the ASP.NET Core API over HTTP.

## Commands

- Dev: `npm run dev`
- Build: `npm run build` — runs `tsc -b` then `vite build`; **type errors fail the build**
- Lint: `npm run lint`
- Preview built output: `npm run preview`

No tests exist yet. Don't claim any pass.

## Conventions

- ESLint flat config in `eslint.config.js` (typescript-eslint + react-hooks + react-refresh).
- ESM only (`"type": "module"`). React 19.
- `dist/` is build output and gitignored — never commit it.
- **shadcn/ui + Tailwind v4** for UI. `@/*` import alias resolves to `src/` (set in `vite.config.ts` + both tsconfigs). Styling is Tailwind utility classes; theme tokens live in `src/index.css` (`@theme`) — the football-green palette is mapped onto shadcn's semantic vars (`--primary`, `--card`, etc.). Add primitives by hand into `components/ui/` (or `npx shadcn@latest add <name>`); they are owned in-repo, not upgraded via npm.

## Structure

Keep a clean, single-purpose component split (dobry podział na komponenty):

- `src/components/ui/` — shadcn primitives only (button, card, badge, …). Don't put feature logic here.
- `src/components/<feature>/` — feature/section components grouped by feature, one component per file (e.g. `components/landing/Hero.tsx`, `Features.tsx`). Pages compose these; sections stay small and focused.
- `src/lib/` — shared helpers (`utils.ts` holds the `cn()` class merger).
- Repeated content (feature cards, steps) → typed arrays mapped in the component, not copy-pasted markup.

## State

`src/` landing page is built (per-section components under `components/landing/`). Routing via `react-router-dom`'s `createBrowserRouter`; route components live under `src/routes/`. No global data-fetching layer yet.

## Auth

- Dev server runs on **`https://localhost:5173`** (self-signed cert via `@vitejs/plugin-basic-ssl`) so the API's `Secure;SameSite=None` cookie flows. Accept the browser cert warning once.
- API base URL: `VITE_API_BASE_URL` (see `.env.development` → `https://localhost:7182`). The API must run on its **https** launch profile.
- All API calls go through `@/lib/api` (`apiFetch`), which sets `credentials: 'include'` and throws `ApiError` on non-2xx.
- Auth state lives in `@/auth/AuthContext` (`AuthProvider` + `useAuth()`). Protected routes are wrapped via `<RequireAuth>` from `@/routes/RequireAuth` — anonymous users get redirected to `/sign-in`.
- E2E smoke: `npm run e2e` runs Playwright (`tests/e2e/auth.spec.ts`). It does **not** auto-start the stack — bring up the API (https profile, `:7182`) and the SPA dev server (`:5173`) before running. The script covers the local email/password round-trip only; Google sign-in is verified manually (Playwright cannot drive Google's consent screen).
