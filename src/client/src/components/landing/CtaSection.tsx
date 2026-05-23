import { Button } from "@/components/ui/button"

export function CtaSection() {
  return (
    <section id="join" className="flex justify-center px-8 py-20">
      <div className="flex w-full max-w-xl flex-col items-center gap-5 text-center">
        <h2>Ready to play?</h2>
        <p className="text-foreground">
          Start your league in under a minute. No credit card, no fuss.
        </p>
        <div className="flex flex-wrap justify-center gap-4">
          <Button asChild size="lg">
            <a href="#">Create a League</a>
          </Button>
          <Button asChild variant="ghost" size="lg">
            <a href="#">Join with Code</a>
          </Button>
        </div>
      </div>
    </section>
  )
}
