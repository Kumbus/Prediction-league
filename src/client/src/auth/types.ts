export interface AuthUser {
  id: string
  email: string
  displayName: string
  isGlobalAdmin: boolean
}

export type AuthStatus = "loading" | "anonymous" | "authenticated" | "error"

export interface AuthState {
  status: AuthStatus
  user: AuthUser | null
}
