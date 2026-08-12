import { useLocation } from "react-router-dom"
import { pathFromLocationState, safeReturnTo } from "@/auth/returnTo"
import { Button } from "@/components/ui/button"
import { apiBaseUrl } from "@/lib/api"

function GoogleLogo() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 48 48"
      aria-hidden="true"
      className="size-4"
    >
      <path
        fill="#FFC107"
        d="M43.611 20.083H42V20H24v8h11.303c-1.649 4.657-6.08 8-11.303 8-6.627 0-12-5.373-12-12s5.373-12 12-12c3.059 0 5.842 1.154 7.961 3.039l5.657-5.657C34.046 6.053 29.268 4 24 4 12.955 4 4 12.955 4 24s8.955 20 20 20 20-8.955 20-20c0-1.341-.138-2.65-.389-3.917z"
      />
      <path
        fill="#FF3D00"
        d="m6.306 14.691 6.571 4.819C14.655 15.108 18.961 12 24 12c3.059 0 5.842 1.154 7.961 3.039l5.657-5.657C34.046 6.053 29.268 4 24 4 16.318 4 9.656 8.337 6.306 14.691z"
      />
      <path
        fill="#4CAF50"
        d="M24 44c5.166 0 9.86-1.977 13.409-5.192l-6.19-5.238C29.211 35.091 26.715 36 24 36c-5.202 0-9.619-3.317-11.283-7.946l-6.522 5.025C9.505 39.556 16.227 44 24 44z"
      />
      <path
        fill="#1976D2"
        d="M43.611 20.083 43.595 20H24v8h11.303a12.04 12.04 0 0 1-4.087 5.571l.003-.002 6.19 5.238C36.971 39.205 44 34 44 24c0-1.341-.138-2.65-.389-3.917z"
      />
    </svg>
  )
}

export function GoogleSignInButton() {
  const location = useLocation()

  const onClick = () => {
    // Google is a full page navigation, so router state cannot survive the round trip — the
    // destination rides in the query string instead.
    const destination =
      pathFromLocationState(location.state) ??
      safeReturnTo(new URLSearchParams(location.search).get("returnTo"))

    // returnUrl still points at /sign-in, never straight at the destination: ExternalCallback
    // reports every external-login failure by appending ?error= to returnUrl, and /sign-in is the
    // only screen that renders those codes. Sending it to a RequireAuth route would bounce an
    // unauthenticated user back here with the error stripped — a silent failure on every Google
    // path, not just invites. /sign-in then forwards to returnTo once the session exists.
    const signInPath = destination
      ? `/sign-in?returnTo=${encodeURIComponent(destination)}`
      : "/sign-in"
    const returnUrl = encodeURIComponent(`${window.location.origin}${signInPath}`)
    window.location.assign(`${apiBaseUrl}/api/auth/login/google?returnUrl=${returnUrl}`)
  }

  return (
    <Button type="button" variant="outline" className="w-full" onClick={onClick}>
      <GoogleLogo />
      Continue with Google
    </Button>
  )
}
