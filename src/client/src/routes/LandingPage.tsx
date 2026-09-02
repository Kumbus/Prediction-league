import { SignOutWarning } from "@/auth/SignOutWarning"
import { CtaSection } from "@/components/landing/CtaSection"
import { Features } from "@/components/landing/Features"
import { Footer } from "@/components/landing/Footer"
import { Hero } from "@/components/landing/Hero"
import { HowItWorks } from "@/components/landing/HowItWorks"
import { Navbar } from "@/components/landing/Navbar"
import { StatsBar } from "@/components/landing/StatsBar"

export function LandingPage() {
  return (
    <div className="flex min-h-svh flex-col">
      {/* Where SignOutButton lands the user. Renders nothing unless the last sign-out failed. */}
      <SignOutWarning />
      <Navbar />
      <Hero />
      <StatsBar />
      <Features />
      <HowItWorks />
      <CtaSection />
      <Footer />
    </div>
  )
}
