import { Navigate, Outlet, useLocation } from "react-router-dom"
import { useAuth } from "@/auth/useAuth"
import { Button } from "@/components/ui/button"

export function RequireAuth() {
  const { status, refresh } = useAuth()
  const location = useLocation()

  if (status === "loading") {
    return (
      <main className="flex min-h-svh items-center justify-center p-6">
        <div
          aria-label="Loading"
          className="size-8 animate-spin rounded-full border-2 border-muted border-t-primary"
        />
      </main>
    )
  }
  if (status === "anonymous") {
    return <Navigate to="/sign-in" state={{ from: location }} replace />
  }
  if (status === "error") {
    return (
      <main className="flex min-h-svh items-center justify-center p-6">
        <div role="alert" className="grid max-w-md gap-3 text-center">
          <h1 className="text-lg font-semibold">Can&apos;t reach the server</h1>
          <p className="text-sm text-muted-foreground">
            We couldn&apos;t verify your session. Check your connection and try again.
          </p>
          <Button onClick={() => void refresh()}>Retry</Button>
        </div>
      </main>
    )
  }
  return <Outlet />
}
