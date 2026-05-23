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

`src/` landing page is built (per-section components under `components/landing/`, composed in `App.tsx`). No router, no data-fetching layer, no API client yet — these are assembled by hand (the chosen Vite-React starter ships none). When wiring API calls, the backend dev URL is `http://localhost:5185`.
