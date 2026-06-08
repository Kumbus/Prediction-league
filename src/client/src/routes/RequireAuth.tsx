import { Navigate, Outlet, useLocation } from "react-router-dom"
import { useAuth } from "@/auth/useAuth"

export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === "loading") return null
  if (status === "anonymous") {
    return <Navigate to="/sign-in" state={{ from: location }} replace />
  }
  return <Outlet />
}
