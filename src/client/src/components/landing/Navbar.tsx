import { Link } from "react-router-dom"
import { useAuth } from "@/auth/useAuth"
import { Button } from "@/components/ui/button"

const links = [
  { href: "#how-it-works", label: "How it works" },
  { href: "#features", label: "Features" },
]

export function Navbar() {
  const { status } = useAuth()
  const isAuthenticated = status === "authenticated"

  return (
    <header className="sticky top-0 z-50 flex h-16 items-center justify-between border-b border-border bg-[rgba(10,31,18,0.95)] px-8 backdrop-blur-md">
      <span className="text-lg font-bold tracking-tight text-white">
        ⚽ Prediction League
      </span>
      <nav className="flex items-center gap-6">
        {links.map((link) => (
          <a
            key={link.href}
            href={link.href}
            className="hidden text-sm text-muted-foreground transition-colors hover:text-white sm:inline"
          >
            {link.label}
          </a>
        ))}
        <Button asChild variant="outline" size="sm">
          <Link to={isAuthenticated ? "/app" : "/sign-in"}>
            {isAuthenticated ? "Open app" : "Sign in"}
          </Link>
        </Button>
      </nav>
    </header>
  )
}
