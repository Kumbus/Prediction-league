import { Navigate, useLocation, useSearchParams } from "react-router-dom"
import { GoogleSignInButton } from "@/auth/GoogleSignInButton"
import { LoginForm } from "@/auth/LoginForm"
import { RegisterForm } from "@/auth/RegisterForm"
import { messageForExternalError } from "@/auth/externalErrors"
import { pathFromLocationState, safeReturnTo } from "@/auth/returnTo"
import { useAuth } from "@/auth/useAuth"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"

export function SignInPage() {
  const { status } = useAuth()
  const location = useLocation()
  const [params] = useSearchParams()
  const externalError = messageForExternalError(params.get("error"))

  // Where sign-in hands the user off. Router state is preferred — it survives the in-SPA email
  // path and cannot be forged from a link — with the `returnTo` query param as the fallback the
  // Google round trip has to use, validated as a same-origin relative path. An invite link is the
  // first destination that actually needs this; without it every path lands on /app.
  const destination =
    pathFromLocationState(location.state) ?? safeReturnTo(params.get("returnTo")) ?? "/app"

  if (status === "loading") {
    return (
      <main className="flex min-h-svh items-center justify-center p-6">
        <div
          aria-label="Loading"
          className="size-8 animate-spin rounded-full border-2 border-muted border-t-primary"
        />
      </main>
    )
  }
  if (status === "authenticated") return <Navigate to={destination} replace />

  return (
    <main className="flex min-h-svh items-center justify-center p-6">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-2xl">Sign in</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-6">
          {externalError && (
            <div
              role="alert"
              className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive"
            >
              {externalError}
            </div>
          )}
          <div data-slot="external-providers">
            <GoogleSignInButton />
          </div>
          <div className="relative">
            <div className="absolute inset-0 flex items-center">
              <span className="w-full border-t border-border" />
            </div>
            <div className="relative flex justify-center">
              <span className="bg-card px-2 text-xs uppercase tracking-wide text-muted-foreground">
                or continue with email
              </span>
            </div>
          </div>
          <Tabs defaultValue="login">
            <TabsList className="grid w-full grid-cols-2">
              <TabsTrigger value="login">Login</TabsTrigger>
              <TabsTrigger value="register">Register</TabsTrigger>
            </TabsList>
            <TabsContent value="login" className="pt-4">
              <LoginForm />
            </TabsContent>
            <TabsContent value="register" className="pt-4">
              <RegisterForm />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>
    </main>
  )
}
