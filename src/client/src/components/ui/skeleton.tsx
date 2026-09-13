import * as React from "react"

import { cn } from "@/lib/utils"

// Placeholder bars for a pending load. `Skeleton` is the bar itself; `SkeletonBlock` wraps a
// group of them and carries the accessible side: the whole region is `aria-busy`, and the
// literal "Loading…" stays in the accessibility tree (visually hidden) so screen readers — and
// any test that waited on that text — still find it.
function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return <div data-slot="skeleton" className={cn("skeleton h-4 w-full", className)} {...props} />
}

function SkeletonBlock({ className, children, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton-block"
      // aria-live rather than role="status": the prediction screens locate their save verdict
      // with getByRole("status"), and a second status region on the page would make that
      // locator ambiguous. The live region still announces the load.
      aria-live="polite"
      aria-busy="true"
      className={cn("grid gap-3", className)}
      {...props}
    >
      <span className="sr-only">Loading…</span>
      {children}
    </div>
  )
}

// The shape a list of cards collapses to while it loads.
function SkeletonCards({ rows = 3 }: { rows?: number }) {
  return (
    <SkeletonBlock>
      {Array.from({ length: rows }, (_, i) => (
        <div
          key={i}
          className="surface rise-in grid gap-3 rounded-xl border border-border bg-card p-6"
          style={{ "--i": i } as React.CSSProperties}
        >
          <Skeleton className="h-5 w-48" />
          <Skeleton className="h-4 w-72 max-w-full" />
        </div>
      ))}
    </SkeletonBlock>
  )
}

export { Skeleton, SkeletonBlock, SkeletonCards }
