namespace PredictionLeague.Application.Predictions;

// One member's forecast for one match, as submitted — before anything has decided whether it is
// a forecast at all. The Api layer maps its own request DTO onto this rather than handing its
// HTTP contract inward: the wire shape is free to change without dragging the rule with it.
//
// Every field is nullable, and the scores are nullable for a reason that is not about being
// optional — they are required. A non-nullable int cannot tell an ABSENT field from a zero, so a
// caller that omitted a score (a typo, a renamed field, a client reading the contract wrong)
// would bind to 0 and store a real-looking goalless draw nobody forecast. Absence has to be
// representable before it can be refused.
public record PredictionItemInput(
    Guid MatchId,
    int? HomeScore,
    int? AwayScore,
    Guid? FirstScorerPlayerId,
    Guid? FirstScorerTeamId,
    int? TotalCards,
    int? YellowCards,
    int? RedCards);
