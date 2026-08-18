namespace PredictionLeague.Application.Abstractions.Persistence;

// One row of a league's table (FR-012). Built from LeagueMembership left-joined to predictions, so
// a member who never predicted appears with zero — the table is the league roster, not just the
// people who played. Display name is resolved by an explicit join in Infrastructure; the Api layer
// never sees an Identity type.
//
// Points sums AwardedPoints, which is null until a match is scored: ScoredMatches counts only the
// non-null ones, so "12 points from 3 matches" is distinguishable from "12 points, 5 predictions,
// 2 not scored yet". Rank is not here — it is assigned server-side by the controller, once, so
// every surface agrees about ties.
public record StandingRowDto(
    Guid UserId,
    string DisplayName,
    int Points,
    int ScoredMatches,
    int PredictionsMade);
