import { Navigate } from "react-router-dom"
import { LoginForm } from "@/auth/LoginForm"
import { RegisterForm } from "@/auth/RegisterForm"
import { useAuth } from "@/auth/useAuth"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"

export function SignInPage() {
  const { status } = useAuth()

  if (status === "loading") return null
  if (status === "authenticated") return <Navigate to="/app" replace />

  return (
    <main className="flex min-h-svh items-center justify-center p-6">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-2xl">Sign in</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-6">
          <div data-slot="external-providers" />
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
