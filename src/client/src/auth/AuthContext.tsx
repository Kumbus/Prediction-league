import { useCallback, useEffect, useMemo, useState } from "react"
import type { ReactNode } from "react"
import { ApiError, apiFetch } from "@/lib/api"
import { AuthContext, type AuthContextValue } from "./auth-context"
import type { AuthState, AuthUser } from "./types"

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: "loading", user: null })

  // No probing.current re-entry guard: in StrictMode the mount effect fires twice and the
  // cleanup aborts the first probe before the second begins. A boolean guard would short-circuit
  // the second call before the first's finally clears it, leaving state stuck on "loading".
  // AbortSignal handles dedup correctly on its own.
  const refresh = useCallback(async (signal?: AbortSignal) => {
    try {
      const user = await apiFetch<AuthUser>("/api/auth/me", { signal })
      if (signal?.aborted) return
      setState({ status: "authenticated", user })
    } catch (err) {
      if (signal?.aborted) return
      if (err instanceof ApiError && err.status === 401) {
        setState({ status: "anonymous", user: null })
      } else {
        console.error("Auth probe failed", err)
        setState((prev) => ({ status: "error", user: prev.user }))
      }
    }
  }, [])

  const signOut = useCallback(async () => {
    try {
      await apiFetch<void>("/api/auth/logout", { method: "POST" })
    } catch (err) {
      console.error("Sign-out request failed", err)
    }
    setState({ status: "anonymous", user: null })
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void refresh(controller.signal)
    return () => controller.abort()
  }, [refresh])

  const value = useMemo<AuthContextValue>(
    () => ({ ...state, refresh, signOut }),
    [state, refresh, signOut],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
