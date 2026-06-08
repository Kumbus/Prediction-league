import { SignOutButton } from "@/auth/SignOutButton"
import { useAuth } from "@/auth/useAuth"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

export function AppShell() {
  const { user } = useAuth()

  return (
    <div className="flex min-h-svh flex-col">
      <header className="sticky top-0 z-50 flex h-16 items-center justify-between border-b border-border bg-[rgba(10,31,18,0.95)] px-8 backdrop-blur-md">
        <span className="text-lg font-bold tracking-tight text-white">
          ⚽ Prediction League
        </span>
        <div className="flex items-center gap-4">
          <span className="text-sm text-muted-foreground">{user?.displayName}</span>
          <SignOutButton />
        </div>
      </header>
      <main className="flex flex-1 items-center justify-center p-6">
        <Card className="w-full max-w-md">
          <CardHeader>
            <CardTitle>You&apos;re signed in</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-muted-foreground">League creation arrives in S-03.</p>
          </CardContent>
        </Card>
      </main>
    </div>
  )
}
