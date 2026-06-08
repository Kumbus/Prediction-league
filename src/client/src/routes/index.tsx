import { createBrowserRouter } from "react-router-dom"
import { AppShell } from "./AppShell"
import { LandingPage } from "./LandingPage"
import { RequireAuth } from "./RequireAuth"
import { SignInPage } from "./SignInPage"

export const router = createBrowserRouter([
  { path: "/", element: <LandingPage /> },
  { path: "/sign-in", element: <SignInPage /> },
  {
    element: <RequireAuth />,
    children: [{ path: "/app", element: <AppShell /> }],
  },
])
