import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { useAuth } from "./useAuth"

export function SignOutButton() {
  const { signOut } = useAuth()
  const navigate = useNavigate()
  const [pending, setPending] = useState(false)

  const onClick = async () => {
    if (pending) return
    setPending(true)
    // Leave the guarded subtree BEFORE the session drops. This button renders inside
    // RequireAuth, which navigates to /sign-in the moment auth status becomes "anonymous"
    // (RequireAuth.tsx:20) — so signing out first lets that redirect win and "/" never
    // renders. signOut never throws and always clears state (AuthContext.tsx), so navigating
    // first cannot park an authenticated session on the landing page. When the request itself
    // failed it records that on the state instead, and the landing page reports it — this button
    // has unmounted with the shell by then and could not.
    navigate("/", { replace: true })
    try {
      await signOut()
    } finally {
      setPending(false)
    }
  }

  return (
    <Button variant="outline" size="sm" onClick={onClick} disabled={pending}>
      {pending ? "Signing out…" : "Sign out"}
    </Button>
  )
}
