import { useCallback, useEffect, useMemo, useState } from "react"
import type { ReactNode } from "react"
import { ApiError, apiFetch } from "@/lib/api"
import { AuthContext, type AuthContextValue } from "./auth-context"
import type { AuthState, AuthUser } from "./types"

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    status: "loading",
    user: null,
    serverSignOutFailed: false,
  })

  // No probing.current re-entry guard: in StrictMode the mount effect fires twice and the
  // cleanup aborts the first probe before the second begins. A boolean guard would short-circuit
  // the second call before the first's finally clears it, leaving state stuck on "loading".
  // AbortSignal handles dedup correctly on its own.
  const refresh = useCallback(async (signal?: AbortSignal) => {
    try {
      const user = await apiFetch<AuthUser>("/api/auth/me", { signal })
      if (signal?.aborted) return
      // A fresh probe supersedes any earlier sign-out verdict.
      setState({ status: "authenticated", user, serverSignOutFailed: false })
    } catch (err) {
      if (signal?.aborted) return
      if (err instanceof ApiError && err.status === 401) {
        setState({ status: "anonymous", user: null, serverSignOutFailed: false })
      } else {
        console.error("Auth probe failed", err)
        setState((prev) => ({ status: "error", user: prev.user, serverSignOutFailed: prev.serverSignOutFailed }))
      }
    }
  }, [])

  // Clearing local state is unconditional on purpose: SignOutButton has already left the guarded
  // subtree by the time this resolves, and refusing to sign out locally would strand the user on a
  // screen they asked to leave. What is *not* unconditional is calling it a success — a request
  // that never reached the server leaves the session cookie alive, and that verdict rides back on
  // the state so the UI can say so and offer a retry.
  const signOut = useCallback(async () => {
    let serverSignOutFailed = false
    try {
      await apiFetch<void>("/api/auth/logout", { method: "POST" })
    } catch (err) {
      console.error("Sign-out request failed", err)
      serverSignOutFailed = true
    }
    setState({ status: "anonymous", user: null, serverSignOutFailed })
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
