export interface AuthUser {
  id: string
  email: string
  displayName: string
  isGlobalAdmin: boolean
}

export type AuthStatus = "loading" | "anonymous" | "authenticated"

export interface AuthState {
  status: AuthStatus
  user: AuthUser | null
}
