import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"

export function Hero() {
  return (
    <section className="relative flex min-h-[600px] items-center justify-center overflow-hidden bg-[radial-gradient(ellipse_80%_60%_at_50%_100%,rgba(26,92,50,0.6)_0%,transparent_70%)] bg-green-dark px-8 pb-20 pt-24">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 [background-image:repeating-linear-gradient(90deg,transparent,transparent_79px,rgba(34,197,94,0.04)_79px,rgba(34,197,94,0.04)_80px),repeating-linear-gradient(0deg,transparent,transparent_79px,rgba(34,197,94,0.04)_79px,rgba(34,197,94,0.04)_80px)]"
      />
      <div className="relative z-10 flex max-w-3xl flex-col items-center gap-6 text-center">
        <Badge>Private. Competitive. Yours.</Badge>
        <h1>
          Predict. Compete.
          <br />
          <span className="text-green-bright">Dominate.</span>
        </h1>
        <p className="max-w-xl text-lg leading-relaxed text-foreground">
          Create a private prediction league for any tournament. Set your own
          scoring rules, invite friends, and watch the standings shift after
          every match.
        </p>
        <div className="flex flex-wrap justify-center gap-4">
          <Button asChild size="lg">
            <a href="#join">Create a League</a>
          </Button>
          <Button asChild variant="ghost" size="lg">
            <a href="#join">Join with Code</a>
          </Button>
        </div>
      </div>
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -bottom-4 right-[8%] rotate-[-20deg] select-none text-[8rem] opacity-[0.07] grayscale-[0.4]"
      >
        ⚽
      </div>
    </section>
  )
}
