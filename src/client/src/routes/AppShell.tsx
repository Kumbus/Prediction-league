import { Link, Outlet } from "react-router-dom"
import { SignOutButton } from "@/auth/SignOutButton"
import { useAuth } from "@/auth/useAuth"

// Persistent chrome for every signed-in screen (logo, admin link, sign-out) — pages under
// /app/* render into the Outlet instead of each repeating their own header. There is no
// standalone "/app" landing screen: it redirects straight to the leagues list.
export function AppShell() {
  const { user } = useAuth()

  return (
    <div className="flex min-h-svh flex-col">
      <header className="sticky top-0 z-50 flex h-16 items-center justify-between border-b border-border bg-[rgba(10,31,18,0.95)] px-8 backdrop-blur-md">
        <Link to="/app/leagues" className="text-lg font-bold tracking-tight text-white">
          ⚽ Prediction League
        </Link>
        <div className="flex items-center gap-4">
          {user?.isGlobalAdmin && (
            <Link to="/admin/tournaments" className="text-sm text-primary hover:underline">
              Admin
            </Link>
          )}
          <span className="text-sm text-muted-foreground">{user?.displayName}</span>
          <SignOutButton />
        </div>
      </header>
      <main className="flex-1">
        <Outlet />
      </main>
    </div>
  )
}
