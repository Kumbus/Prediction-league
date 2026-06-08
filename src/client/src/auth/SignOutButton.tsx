import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { useAuth } from "./useAuth"

export function SignOutButton() {
  const { signOut } = useAuth()
  const navigate = useNavigate()

  const onClick = async () => {
    await signOut()
    navigate("/", { replace: true })
  }

  return (
    <Button variant="outline" size="sm" onClick={onClick}>
      Sign out
    </Button>
  )
}
