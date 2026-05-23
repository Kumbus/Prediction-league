import { CtaSection } from "@/components/landing/CtaSection"
import { Features } from "@/components/landing/Features"
import { Footer } from "@/components/landing/Footer"
import { Hero } from "@/components/landing/Hero"
import { HowItWorks } from "@/components/landing/HowItWorks"
import { Navbar } from "@/components/landing/Navbar"
import { StatsBar } from "@/components/landing/StatsBar"

function App() {
  return (
    <div className="flex min-h-svh flex-col">
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

export default App
