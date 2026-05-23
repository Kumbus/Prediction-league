const stats = [
  { value: "100%", label: "Private leagues" },
  { value: "Custom", label: "Scoring rules" },
  { value: "Any", label: "Tournament" },
  { value: "Live", label: "Standings" },
]

export function StatsBar() {
  return (
    <section className="flex flex-wrap justify-center border-y border-border bg-green-mid">
      {stats.map((stat) => (
        <div
          key={stat.label}
          className="flex flex-1 basis-[45%] flex-col items-center gap-1 border-r border-border px-4 py-5 last:border-r-0 sm:basis-auto sm:px-12 sm:py-6"
        >
          <strong className="text-2xl font-bold text-green-bright">
            {stat.value}
          </strong>
          <span className="text-xs uppercase tracking-wide text-muted-foreground">
            {stat.label}
          </span>
        </div>
      ))}
    </section>
  )
}
