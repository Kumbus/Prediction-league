import { ArrowRight } from "lucide-react"

const steps = [
  {
    title: "Create a League",
    body: "Pick a tournament, set your scoring rules, and get an invite code in seconds.",
  },
  {
    title: "Invite Friends",
    body: "Share the code. Anyone with it can join — no approval queue.",
  },
  {
    title: "Predict & Win",
    body: "Submit predictions before each match. Earn points, climb the table, talk trash.",
  },
]

export function HowItWorks() {
  return (
    <section
      id="how-it-works"
      className="border-y border-border bg-green-mid px-8 py-20 text-center"
    >
      <h2 className="mb-12">How it works</h2>
      <div className="mx-auto flex max-w-4xl flex-col items-center justify-center gap-4 md:flex-row md:items-start">
        {steps.map((step, i) => (
          <div key={step.title} className="contents md:flex md:flex-1 md:items-start">
            <div className="flex flex-1 flex-col items-center gap-3 p-6">
              <div className="flex size-12 items-center justify-center rounded-full border-2 border-green-bright bg-green-bright/15 text-xl font-bold text-green-bright">
                {i + 1}
              </div>
              <h3 className="text-white">{step.title}</h3>
              <p className="text-sm leading-relaxed text-foreground">{step.body}</p>
            </div>
            {i < steps.length - 1 && (
              <ArrowRight className="my-2 size-6 shrink-0 rotate-90 text-muted-foreground md:mt-7 md:rotate-0" />
            )}
          </div>
        ))}
      </div>
    </section>
  )
}
