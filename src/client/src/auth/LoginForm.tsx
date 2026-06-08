import { zodResolver } from "@hookform/resolvers/zod"
import { useState } from "react"
import { useForm } from "react-hook-form"
import { useLocation, useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form"
import { Input } from "@/components/ui/input"
import { ApiError, apiFetch } from "@/lib/api"
import { useAuth } from "./useAuth"
import { loginSchema, type LoginFormValues } from "./schemas"

interface LocationState {
  from?: { pathname?: string }
}

export function LoginForm() {
  const { refresh } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [formError, setFormError] = useState<string | null>(null)

  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  })

  const onSubmit = async (values: LoginFormValues) => {
    setFormError(null)
    try {
      await apiFetch<void>("/api/auth/login", { method: "POST", body: values })
      await refresh()
      const from = (location.state as LocationState | null)?.from?.pathname
      const safeFrom = from && from.startsWith("/") && !from.startsWith("//") ? from : "/app"
      navigate(safeFrom, { replace: true })
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setFormError("Invalid email or password.")
        return
      }
      if (err instanceof ApiError) {
        setFormError(err.problem?.title ?? err.message ?? "Sign-in failed.")
        return
      }
      setFormError("Sign-in failed. Please try again.")
    }
  }

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmit)} className="grid gap-4">
        <FormField
          control={form.control}
          name="email"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Email</FormLabel>
              <FormControl>
                <Input type="email" autoComplete="email" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="password"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Password</FormLabel>
              <FormControl>
                <Input type="password" autoComplete="current-password" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        {formError && (
          <p role="alert" className="text-sm text-destructive">
            {formError}
          </p>
        )}
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </Form>
  )
}
