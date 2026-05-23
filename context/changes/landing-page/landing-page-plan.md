# Plan: Adopt shadcn/ui + reimplement client landing page

## Context

`client/` is a fresh React 19 + Vite + TS SPA. The landing page (`src/App.tsx` + `App.css` + `index.css`) is hand-rolled CSS with a dark football-green theme. We want a real component library so future member screens (standings table, prediction forms) are built on accessible, agent-editable primitives.

Decision: **shadcn/ui** — components are copied into the repo (you own the code, agent edits files directly), built on Radix + Tailwind, React 19 ready. Matches the repo's "own your code / agent-readable" posture.

User choices: **keep the existing dark football-green theme**, and **split the landing into per-section components** (good folder structure / dobry podział na komponenty).

Constraint: shadcn/ui requires Tailwind, a `@/*` path alias, and a `components.json`. None exist yet — this is net-new setup.

## 1. Install + configure Tailwind v4

- Add deps: `tailwindcss`, `@tailwindcss/vite` (Tailwind v4 Vite plugin), and shadcn's runtime deps (`class-variance-authority`, `clsx`, `tailwind-merge`, `lucide-react`, `tw-animate-css`).
- `src/client/vite.config.ts`: add `tailwindcss()` plugin and the `@` → `./src` alias (`resolve.alias`). `@types/node` already present for `path`.
- `src/client/tsconfig.json` + `tsconfig.app.json`: add `compilerOptions.baseUrl: "."` and `paths: { "@/*": ["./src/*"] }` so editor + `tsc -b` resolve `@/`.

## 2. Theme tokens (preserve green look)

- Replace `src/index.css` with Tailwind v4 entry: `@import "tailwindcss";` + `@theme` block mapping the existing palette from current `index.css` (`--green-dark #0a1f12`, `--green-mid #12382a`, `--green-field #1a5c32`, `--green-bright #22c55e`, `--green-light #4ade80`, `--gold #f59e0b`, text greens, border) into shadcn's CSS-variable contract (`--background`, `--foreground`, `--primary`, `--card`, `--border`, `--muted`, etc.). Dark green = background, `--green-bright` = primary, gold = accent.
- Keep base typography (clamp h1/h2, system-ui font) via `@layer base`.

## 3. shadcn init + add components

- `npx shadcn@latest init` → writes `components.json` (style: new-york, base color slate but overridden by our tokens, css var mode, alias `@/components`).
- Add primitives the landing needs: `button`, `card`, `badge`. These land in `src/components/ui/`.

## 4. Folder structure (per-section split)

```
src/
  components/
    ui/            # shadcn primitives (button, card, badge)
    landing/       # one file per section
      Navbar.tsx
      Hero.tsx
      StatsBar.tsx
      Features.tsx
      HowItWorks.tsx
      CtaSection.tsx
      Footer.tsx
  lib/
    utils.ts       # shadcn cn() helper
  App.tsx          # composes the landing sections
```

- Each section in `App.css` maps to one component. Port markup from current `App.tsx` (lines per section already separated) into these files using shadcn `Button`/`Card`/`Badge` + Tailwind utility classes instead of the bespoke `.btn`/`.feature-card` CSS.
- Features/steps data → small typed arrays mapped in the component (no hardcoded repetition).
- Delete `App.css` once sections are ported; remove unused `src/assets/hero.png` only if it ends up unreferenced (leave if used).

## 5. Docs updates

- **`context/foundation/tech-stack.md`** — in "Why this stack", note the React frontend uses **shadcn/ui (Radix + Tailwind v4)** as the component layer; this fills the gap the note calls out ("vite-react is light on baked-in conventions"). Components are vendored into the repo, not a runtime dependency.
- **Root `AGENTS.md`** — under `client/` layout bullet, add that UI is built with shadcn/ui + Tailwind v4; primitives live in `client/src/components/ui/` and are owned/edited in-repo (not upgraded via npm).
- **`src/client/AGENTS.md`** — update "State" + add a "Structure" section:
  - shadcn/ui + Tailwind v4 in use; `@/*` alias → `src/`.
  - Component-division rule: shadcn primitives in `components/ui/`, feature/section components grouped by feature (e.g. `components/landing/`), shared helpers in `lib/`. One component per file, keep them small and single-purpose. (Captures the requested "very good folder structure / dobry podział na komponenty".)

## Verification

- `cd src/client && npm install`
- `npm run build` — `tsc -b` must pass (type errors fail build); confirms alias + types resolve.
- `npm run dev` → open `http://localhost:5173`, confirm landing renders with the green theme, all sections present, buttons/cards styled, responsive at mobile width.
- `npm run lint` clean.
