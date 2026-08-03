import type { ScoringParameter, ScoringRuleDto } from "@/leagues/types"
import { MAX_RULE_POINTS, MIN_RULE_POINTS, SCORING_DEFAULTS } from "@/leagues/types"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

// "Pick the parameters that score and what each is worth" — shared by the create form and the
// detail page's edit mode so the two can't drift. The value *is* the submittable rule set:
// absence of a parameter means it does not score, which is exactly the server's contract.
// Points are not preserved across a toggle — deactivating drops the row, reactivating starts
// from the catalogue default.

// Display order comes from the catalogue, not from the order the user ticked boxes in.
const ORDER = new Map(SCORING_DEFAULTS.map((d, index) => [d.parameter, index]))

interface ScoringRulesFieldsetProps {
  value: ScoringRuleDto[]
  onChange: (rules: ScoringRuleDto[]) => void
  disabled?: boolean
}

export function ScoringRulesFieldset({ value, onChange, disabled }: ScoringRulesFieldsetProps) {
  const active = new Map(value.map((r) => [r.parameter, r.points]))

  const emit = (rules: ScoringRuleDto[]) =>
    onChange([...rules].sort((a, b) => (ORDER.get(a.parameter) ?? 0) - (ORDER.get(b.parameter) ?? 0)))

  const toggle = (parameter: ScoringParameter, checked: boolean) => {
    if (!checked) {
      emit(value.filter((r) => r.parameter !== parameter))
      return
    }
    const fallback = SCORING_DEFAULTS.find((d) => d.parameter === parameter)?.points ?? MIN_RULE_POINTS
    emit([...value, { parameter, points: fallback }])
  }

  const setPoints = (parameter: ScoringParameter, points: number) =>
    emit(value.map((r) => (r.parameter === parameter ? { ...r, points } : r)))

  return (
    <fieldset className="grid gap-3" disabled={disabled}>
      <legend className="text-sm font-medium">Scoring</legend>
      <p className="text-sm text-muted-foreground">
        Tick the parameters this league scores and set what each is worth. Unticked parameters do
        not score at all.
      </p>
      <div className="grid gap-3 sm:grid-cols-2">
        {SCORING_DEFAULTS.map((d) => {
          const isActive = active.has(d.parameter)
          return (
            <div key={d.parameter} className="grid gap-2">
              <div className="flex items-center gap-2">
                {/* No checkbox primitive is vendored in components/ui — a native input styled
                    with Tailwind beats adding a shadcn dependency for one control. */}
                <input
                  id={`scoring-active-${d.parameter}`}
                  type="checkbox"
                  className="size-4 accent-primary"
                  checked={isActive}
                  onChange={(e) => toggle(d.parameter, e.target.checked)}
                />
                <Label htmlFor={`scoring-active-${d.parameter}`}>{d.label}</Label>
              </div>
              <Input
                id={`scoring-points-${d.parameter}`}
                type="number"
                min={MIN_RULE_POINTS}
                max={MAX_RULE_POINTS}
                aria-label={`${d.label} points`}
                // A disabled input is skipped by native validation, so an inactive parameter
                // can never block the submit it is not part of.
                disabled={!isActive}
                required={isActive}
                // 0 is not a legal points value, so it stands in for "cleared" — render it as
                // an empty field rather than snapping a 0 under the organizer's cursor. Either
                // way `required` + min block the submit.
                value={(active.get(d.parameter) ?? d.points) || ""}
                onChange={(e) =>
                  setPoints(
                    d.parameter,
                    Number.isNaN(e.target.valueAsNumber) ? 0 : e.target.valueAsNumber,
                  )
                }
              />
            </div>
          )
        })}
      </div>
    </fieldset>
  )
}
