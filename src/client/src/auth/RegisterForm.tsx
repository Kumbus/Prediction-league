import { zodResolver } from "@hookform/resolvers/zod"
import { useState } from "react"
import { useForm } from "react-hook-form"
import { useNavigate } from "react-router-dom"
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
import { registerSchema, type RegisterFormValues } from "./schemas"

const SERVER_FIELD_MAP: Record<string, keyof RegisterFormValues> = {
  Email: "email",
  email: "email",
  DuplicateEmail: "email",
  DuplicateUserName: "email",
  InvalidEmail: "email",
  Password: "password",
  password: "password",
  PasswordTooShort: "password",
  PasswordRequiresDigit: "password",
  PasswordRequiresLower: "password",
  PasswordRequiresUpper: "password",
  PasswordRequiresNonAlphanumeric: "password",
  PasswordRequiresUniqueChars: "password",
  DisplayName: "displayName",
  displayName: "displayName",
}

export function RegisterForm() {
  const { refresh } = useAuth()
  const navigate = useNavigate()
  const [formError, setFormError] = useState<string | null>(null)

  const form = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { email: "", password: "", displayName: "" },
  })

  const onSubmit = async (values: RegisterFormValues) => {
    setFormError(null)
    try {
      await apiFetch<void>("/api/auth/register", { method: "POST", body: values })
      await refresh()
      navigate("/app", { replace: true })
    } catch (err) {
      if (err instanceof ApiError) {
        const errors = err.problem?.errors
        let mapped = false
        if (errors) {
          for (const [key, messages] of Object.entries(errors)) {
            const field = SERVER_FIELD_MAP[key]
            const message = messages?.[0]
            if (field && message) {
              form.setError(field, { type: "server", message })
              mapped = true
            }
          }
        }
        if (!mapped) {
          setFormError(err.problem?.title ?? err.message ?? "Could not create account.")
        }
        return
      }
      setFormError("Could not create account. Please try again.")
    }
  }

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmit)} className="grid gap-4">
        <FormField
          control={form.control}
          name="displayName"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Display name</FormLabel>
              <FormControl>
                <Input autoComplete="nickname" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
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
                <Input type="password" autoComplete="new-password" {...field} />
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
          {form.formState.isSubmitting ? "Creating account…" : "Create account"}
        </Button>
      </form>
    </Form>
  )
}
