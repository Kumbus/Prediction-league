# Follow-ups — submit-locked-predictions impl review (2026-08-15)

Queued during triage of `reviews/impl-review.md`. Everything else was fixed in place.

## Notify members when a match they predicted is deleted (from F3)

Deleting a match cascades away every member's forecast for it — accepted deliberately: an admin fixing a typo should edit the match, not delete and recreate it, and a match that genuinely disappears takes its predictions with it. What is missing is that the members never find out.

When notifications exist (not in this MVP slice per the PRD), tell every member whose forecast was destroyed by a match deletion. Until then the deletion stays silent.

- Trigger site: `TournamentsController.DeleteMatch` (`src/server/PredictionLeague.Api/Controllers/TournamentsController.cs`)
- Related: `PredictionConfiguration.cs` — the Match FK cascade that makes this happen
