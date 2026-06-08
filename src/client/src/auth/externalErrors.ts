export const EXTERNAL_ERROR_MESSAGES: Record<string, string> = {
  external_login_failed: "Google sign-in didn't complete. Please try again.",
  no_email: "Your Google account did not return an email address.",
  account_exists:
    "An account with that email already exists. Sign in with email and password instead.",
  provisioning_failed: "We could not create your account. Please try again.",
  link_failed: "We could not link your Google account. Please try again.",
}

export function messageForExternalError(code: string | null): string | null {
  if (!code) return null
  return EXTERNAL_ERROR_MESSAGES[code] ?? "Sign-in failed."
}
