# Selective visual review — prediction and standings screens

> Second half of `context/foundation/test-plan.md` §3 Phase 4.
> Date: 2026-09-02 · Reviewer: Kumbus (with Claude) · Commit: phase 4 (`ce184f9`)

## Why this exists, and what it is not

Risk #6 asks whether a member, **looking at the screen**, can answer three
questions. An assertion can prove a string is present; it cannot prove the string
is readable, findable, or unambiguous in context. That gap is the entire scope of
this review.

Per test-plan §4, this is **selective by design**: two screens, once, recorded.
It is **not** a per-page sweep, **not** merge-blocking, and **not** layered over
anything a deterministic assertion already catches — the four rendered states are
covered by `predictions-legibility.spec.ts` and are not re-litigated here.

## The prompt

Given only a screenshot of the screen, with no knowledge of the codebase, answer:

1. **What can I still change?**
2. **What did I save?**
3. **What is it worth?**

Then: for anything you could not answer, say what on the screen misled you or was
missing — not what the code does.

## What was reviewed

Full-page screenshots at 1280px wide, captured from the live app with the fixture
member's session:

- **League A predictions**, in the mixed state the fixture produces: one editable
  match, one kicked-off-and-scored, one kicked-off-and-unscored, one kicked-off
  and never forecast.
- **League A standings**, after one match had been scored.

Screenshots were captured with a throwaway script and deliberately not committed —
the phase chose a scripted manual pass over wiring capture into the specs, because
capturing is not reviewing.

## Findings

### 1. The editable row is identified only by absence — MEDIUM

Every locked row carries a status badge (`FINISHED 2–1`, `LIVE`) and the line
`Locked at kickoff.`. The one row that is still open carries **neither** — no
badge, no "still open" copy. Its editability is signalled only by the presence of
two input boxes and the absence of everything else.

This is the first of the risk's three questions ("what can I still change?"), and
the screen answers it by omission. A member scanning quickly sees three rows with
badges and one without. An explicit affordance — `Open until 3.09.2026, 09:37`,
or an `OPEN` badge in the slot the others use — would make editability a positive
statement.

No assertion can express this: `getByLabel(...).fill(...)` passes exactly as well
either way.

### 2. The copy that explains *why* a row is inert is the least legible on the card — MEDIUM

`Locked at kickoff.` and `You did not forecast this match.` render in
`text-muted-foreground`, which against the dark-green card is materially dimmer
than the forecast line directly beneath it. The explanation is styled as
subordinate to the thing it explains.

`toBeVisible()` passes regardless of contrast, so this is exactly the class of
defect the review exists to catch. Worth a contrast check against WCAG AA before
deciding whether it is a real problem or an aesthetic preference.

### 3. An unscored match is silent, but never says scoring is still pending — LOW

The deliberate design (`MatchPredictionRow.tsx:218-222`) is that a finished-but-
unscored forecast shows nothing rather than a `0` that would read as a verdict.
Reviewing the screen, that is the right call — `Your forecast: 3–0` with no points
does not mislead.

But it also does not reassure. The standings screen has copy for this state
("Points appear as soon as a match finishes and its result is entered"); the row
does not. The `LIVE` badge partly covers it. Silence is better than a false zero,
yet a member could still wonder whether scoring failed.

### 4. The scored row states its points twice — LOW

`Your forecast: 2–1 · 5 pts`, and then again right-aligned in the reveal panel as
`5 pts`. Correct, but with a single member it reads as duplication. With a real
league the reveal panel becomes a per-match scoreboard and the repetition is
justified, so this may resolve itself at realistic member counts.

### 5. Standings shows a total with no way to understand it — MEDIUM (risk #2's user-facing half)

The table gives `Points 5` and `Matches scored 1` and nothing else. The league's
scoring rules are not shown on this screen. A member in two leagues on one
tournament sees 5 here and 3 there for **the same forecasts**, and the screens
offer nothing to explain the difference.

The divergence is exactly what risk #2 wants to be true, and
`league-scoring-divergence.spec.ts` proves it *is* true. But the product's wedge
is invisible where members actually look: a member is as likely to read the
difference as a bug as a feature. Surfacing the rule set on standings — even a
line like "This league scores: Exact score (5)" — would turn a confusing number
into the feature it is.

### 6. Long horizontal scan between member and points — LOW

At 1280px the member name sits at the far left and `Points` at the far right, with
empty space between. With one row this is harmless; with fifteen members and no
zebra striping or leader line, reading across to the wrong row becomes plausible.

## Verdict

Nothing here contradicts the automated coverage: all four prediction states render
as the specs assert, and the standings totals match the oracle (5 and 3).

The findings are about **legibility, not correctness**, which is what this review
is for. Two are worth a follow-up change if risk #6 is judged still live —
**finding 1** (editability signalled by absence) and **finding 5** (a total with
no explanation) — because each maps directly onto one of the three questions the
risk asks. Findings 2, 3, 4 and 6 are lower-stakes polish.

None of these block this change. Opening them is a product decision, not a testing
one.
