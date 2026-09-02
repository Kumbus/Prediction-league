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
  // True when the last sign-out could not reach the server.
  //
  // The auth cookie is HttpOnly and only SignInManager.SignOutAsync clears it, so the client
  // cannot end the session itself. Dropping local state to "anonymous" is a safe UI default, not
  // proof the session ended: on a failed request the cookie stays valid, and the next /api/auth/me
  // signs the user straight back in. Without this flag that reads as "sign-out worked, then the app
  // randomly signed me back in" — the same shape as any other operation that reports success while
  // its persistent effect never happened.
  serverSignOutFailed: boolean
}
