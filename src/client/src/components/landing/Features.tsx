import { BarChart3, Globe, Link2, Trophy, type LucideIcon } from "lucide-react"

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

type Feature = {
  icon: LucideIcon
  title: string
  body: string
}

const features: Feature[] = [
  {
    icon: Trophy,
    title: "Custom Scoring",
    body: "Award points for exact scores, correct outcomes, goal scorers, or card counts. Every league plays by its own rules.",
  },
  {
    icon: Link2,
    title: "Invite by Code",
    body: "Share a private invite code. No public sign-ups, no randos — just your crew competing for bragging rights.",
  },
  {
    icon: BarChart3,
    title: "Live Standings",
    body: "Standings update automatically after each match result. No manual tallying, no spreadsheet arguments.",
  },
  {
    icon: Globe,
    title: "Any Tournament",
    body: "World Cup, Euros, domestic cups — if there are matches, you can run a prediction league around it.",
  },
]

export function Features() {
  return (
    <section id="features" className="mx-auto w-full max-w-5xl px-6 py-20 text-center">
      <h2 className="mb-12">Everything your league needs</h2>
      <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
        {features.map(({ icon: Icon, title, body }) => (
          <Card
            key={title}
            className="border-border text-left transition-all hover:-translate-y-0.5 hover:border-green-bright/35"
          >
            <CardHeader>
              <Icon className="mb-2 size-8 text-green-bright" />
              <CardTitle className="text-lg text-white">{title}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm leading-relaxed text-foreground">{body}</p>
            </CardContent>
          </Card>
        ))}
      </div>
    </section>
  )
}
