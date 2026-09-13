import { Link, Outlet, useLocation } from "react-router-dom"
import { SignOutButton } from "@/auth/SignOutButton"
import { useAuth } from "@/auth/useAuth"

// Persistent chrome for every signed-in screen (logo, admin link, sign-out) — pages under
// /app/* render into the Outlet instead of each repeating their own header. There is no
// standalone "/app" landing screen: it redirects straight to the leagues list.
export function AppShell() {
  const { user } = useAuth()
  // Keying the Outlet wrapper on the path remounts it per navigation, which is what replays
  // the entrance animation; without the key React reuses the node and nothing animates.
  const { pathname } = useLocation()

  return (
    <div className="flex min-h-svh flex-col bg-[radial-gradient(ellipse_70%_50%_at_50%_-10%,rgba(26,92,50,0.45)_0%,transparent_70%)]">
      <header className="sticky top-0 z-50 border-b border-border bg-[rgba(10,31,18,0.82)] backdrop-blur-xl">
        <div className="mx-auto flex h-16 w-full max-w-6xl items-center justify-between gap-4 px-6">
          <Link
            to="/app/leagues"
            className="text-lg font-bold tracking-tight text-white transition-colors hover:text-green-light"
          >
            ⚽ Prediction League
          </Link>
          <div className="flex items-center gap-3">
            {user?.isGlobalAdmin && (
              <Link
                to="/admin/tournaments"
                className="rounded-full border border-border px-3 py-1 text-sm text-primary transition-colors hover:border-green-bright hover:bg-green-bright/10"
              >
                Admin
              </Link>
            )}
            <span className="hidden text-sm text-muted-foreground sm:inline">
              {user?.displayName}
            </span>
            <SignOutButton />
          </div>
        </div>
      </header>
      <main className="flex-1">
        <div key={pathname} className="page-in mx-auto w-full max-w-6xl">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
