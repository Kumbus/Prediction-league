import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import type { ReactNode } from "react"
import { ApiError, apiFetch } from "@/lib/api"
import { AuthContext, type AuthContextValue } from "./auth-context"
import type { AuthState, AuthUser } from "./types"

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: "loading", user: null })
  const probing = useRef(false)

  const refresh = useCallback(async (signal?: AbortSignal) => {
    if (probing.current) return
    probing.current = true
    try {
      const user = await apiFetch<AuthUser>("/api/auth/me", { signal })
      setState({ status: "authenticated", user })
    } catch (err) {
      if (signal?.aborted) return
      if (err instanceof ApiError && err.status === 401) {
        setState({ status: "anonymous", user: null })
      } else {
        console.error("Auth probe failed", err)
        setState((prev) => ({ status: "error", user: prev.user }))
      }
    } finally {
      probing.current = false
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
