import { cn } from "@/lib/utils"

// The position chip in a standings row. The top three are the only ranks that get a colour —
// gold, silver, bronze — and everything below stays neutral, so the podium is legible at a
// glance without the table turning into a rainbow.
//
// Colour is never the only carrier: the number itself is the rank, so a monochrome or
// colour-blind reading loses nothing.

const PODIUM: Record<number, string> = {
  1: "border-gold/50 bg-gold/15 text-gold-light",
  2: "border-slate-300/40 bg-slate-300/10 text-slate-200",
  3: "border-amber-700/50 bg-amber-700/15 text-amber-500",
}

interface RankBadgeProps {
  rank: number
  className?: string
}

export function RankBadge({ rank, className }: RankBadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex size-7 shrink-0 items-center justify-center rounded-full border text-xs font-semibold tabular-nums",
        PODIUM[rank] ?? "border-border bg-white/5 text-muted-foreground",
        className,
      )}
    >
      {rank}
    </span>
  )
}
