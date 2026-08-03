import { Link, useNavigate } from "react-router-dom"
import { SignOutButton } from "@/auth/SignOutButton"
import { useAuth } from "@/auth/useAuth"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

export function AppShell() {
  const { user } = useAuth()
  const navigate = useNavigate()

  return (
    <div className="flex min-h-svh flex-col">
      <header className="sticky top-0 z-50 flex h-16 items-center justify-between border-b border-border bg-[rgba(10,31,18,0.95)] px-8 backdrop-blur-md">
        <span className="text-lg font-bold tracking-tight text-white">
          ⚽ Prediction League
        </span>
        <div className="flex items-center gap-4">
          {user?.isGlobalAdmin && (
            <Link to="/admin/tournaments" className="text-sm text-primary hover:underline">
              Admin
            </Link>
          )}
          <span className="text-sm text-muted-foreground">{user?.displayName}</span>
          <SignOutButton />
        </div>
      </header>
      <main className="flex flex-1 items-center justify-center p-6">
        <Card className="w-full max-w-md">
          <CardHeader>
            <CardTitle>You&apos;re signed in</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4">
            <p className="text-muted-foreground">
              Create a league for a tournament and invite your friends.
            </p>
            <Button onClick={() => navigate("/app/leagues")}>My leagues</Button>
          </CardContent>
        </Card>
      </main>
    </div>
  )
}
