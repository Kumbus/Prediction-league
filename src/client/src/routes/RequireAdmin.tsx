import { Navigate, Outlet, useLocation } from "react-router-dom"
import { useAuth } from "@/auth/useAuth"
import { Button } from "@/components/ui/button"

export function RequireAdmin() {
  const { status, user, refresh } = useAuth()
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
          <Button onClick={() => void refresh()}>Retry</Button>
        </div>
      </main>
    )
  }
  if (!user?.isGlobalAdmin) {
    return <Navigate to="/app?denied=admin" replace />
  }
  return <Outlet />
}
