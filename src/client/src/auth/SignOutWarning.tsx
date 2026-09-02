import { useState } from "react"
import { Button } from "@/components/ui/button"
import { useAuth } from "./useAuth"

// Shown on the landing page after a sign-out whose request never reached the server.
//
// The app looks signed out because local state says so, but the HttpOnly auth cookie is still
// valid and the next probe would sign the user back in. Only the server can end that session, so
// the one useful action is to try the call again — hence a retry rather than a bare notice.
export function SignOutWarning() {
  const { serverSignOutFailed, signOut } = useAuth()
  const [retrying, setRetrying] = useState(false)

  if (!serverSignOutFailed) return null

  const retry = async () => {
    if (retrying) return
    setRetrying(true)
    try {
      await signOut()
    } finally {
      setRetrying(false)
    }
  }

  return (
    <div
      role="alert"
      className="flex flex-wrap items-center justify-center gap-3 border-b border-destructive/40 bg-destructive/10 px-6 py-3 text-sm text-destructive"
    >
      <span>
        We couldn&apos;t reach the server to sign you out — your session may still be open on this
        device.
      </span>
      <Button variant="outline" size="sm" onClick={retry} disabled={retrying}>
        {retrying ? "Trying again…" : "Try again"}
      </Button>
    </div>
  )
}
