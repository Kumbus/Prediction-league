import type { CSSProperties } from "react"
import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

// Feeds the `--i` the `.rise-in` class reads for its animation-delay, so a list enters in
// sequence instead of all at once. Capped: past a dozen rows the tail would just look slow.
export function stagger(index: number): CSSProperties {
  return { "--i": Math.min(index, 12) } as CSSProperties
}
