# Seeded accounts — how to sign in

`seed-real-league.mjs` creates the accounts it needs and then prints them. This file is the
reference for what it creates and how to get into each one, so you do not have to re-read the
script or scroll back through a terminal.

## The short version

Open the SPA's sign-in page, stay on the **Login** tab, and use:

| | |
|---|---|
| Email | `demo1@example.test` … `demo6@example.test` |
| Password | `Password123!` |

`demo1` is the organizer. The rest are ordinary members.

Locally the sign-in page is <https://localhost:5173/sign-in>. On a deployed instance it is
`/sign-in` on whatever host the SPA is served from.

## What the script creates

**Six member accounts.** Email `demo<n>@example.test`, display name `Demo Member <n>`, password
`Password123!`. They are plain members with no admin rights — `/admin` is not theirs to open.

- `--members <n>` changes how many exist.
- `--member-prefix <word>` changes the local part, so `--member-prefix tester` gives
  `tester1@example.test`. Handy for seeding a second, separate pool without disturbing the first.
- `--member-password <password>` changes the password for accounts created from then on.

**One admin account.** `e2e-admin@example.test` / `Password123!` — the same account the E2E suite
uses. It owns the tournament, the teams and every match the script writes. Sign in as this one to
reach the admin screens (Admin link in the header, `/admin/tournaments`).

`--admin-email` and `--admin-password` point the script at a different admin, which is what you
want against a deployed instance.

**One league.** Named after the competition — `Premier League Pool` by default, `La Liga Pool`
under `--competition es.1`, and so on. Organized by `demo1`, with every other member joined. The
script prints its invite code when it finishes.

## Signing in as yourself instead

You do not need a demo account to look at the league — join it with your own:

1. Sign in (or register) with your own address.
2. **Join a league** on the leagues screen.
3. Paste the invite code the script printed.

Lost the code? Any member can read it off the league page — it is the first card, next to **Copy
invite link**. Re-running the script is not the way to get it back; that creates a second league.

## Re-running the script

Accounts are created once and signed into on every run after that, so the credentials above stay
the same. Passwords are never reset — if you change `--member-password` after a first run, the
existing accounts keep the password they were created with, and the script will fail to sign in
with the new one.

Each run creates a **new** tournament and a **new** league. It does not clean up the previous one.
To remove an old pool, have every member leave it — leaving as the sole member is the only path
that deletes a league — and then delete its tournament as the admin. Teams are global reference
data with no delete route; the next run reuses them, which is intended.

## Admin rights are configuration, not something the script can grant

The admin address must already be in the API's `Admin:Emails` allowlist. The script registers the
account if it is missing, but registration alone does not make it an admin — the claim is read
from the allowlist when the principal is built.

Locally that allowlist lives in `src/server/PredictionLeague.Api/appsettings.Development.json`,
with `e2e-admin@example.test` at index 0. **Configuration merges arrays by index and user-secrets
load after appsettings**, so a personal admin address put at `Admin:Emails:0` silently replaces the
E2E one. Put yours at index 1 or higher.

A further trap worth knowing when an account *looks* signed in but every admin action still 403s:
the admin claim is baked into the cookie when the principal is built and is never re-derived per
request. Adding an address to the allowlist does not promote a session that already exists — sign
out and back in.

## If sign-in fails locally

- **The page will not load, or the app cannot reach the API.** Both dev servers use self-signed
  certificates. The SPA is on `https://localhost:5173` and the API on `https://localhost:7182`, and
  the browser has to be told to trust each of them; visiting both once and accepting the warning is
  enough. An app that renders but reports every call as failed usually means the API's certificate
  has not been accepted yet.
- **Credentials rejected.** The account may not exist in the database you are pointing at — the
  script creates accounts in whichever API `--api` names, so a member seeded against localhost does
  not exist in Azure.
- **Signed in, but no leagues.** Fine and expected if you registered yourself rather than using a
  demo account: join with the invite code above.
