import { createContext } from "react"
import type { AuthState } from "./types"

export interface AuthContextValue extends AuthState {
  refresh: (signal?: AbortSignal) => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)
