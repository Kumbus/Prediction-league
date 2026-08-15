namespace PredictionLeague.Application.Abstractions.Persistence;

// Read-side projection backing the post-kickoff reveal (FR-009). Prediction.UserId is a bare Guid
// with no FK to AspNetUsers, so the display name is resolved by an explicit join in Infrastructure
// — the Api layer never sees an Identity type. Scorer and credited-team names come along so the
// client can render an own-goal pick (player from one team, goal credited to the other) as text.
public record MemberPredictionDto(
    Guid MatchId,
    Guid UserId,
    string DisplayName,
    int PredictedHomeScore,
    int PredictedAwayScore,
    Guid? FirstScorerPlayerId,
    string? FirstScorerName,
    Guid? FirstScorerTeamId,
    string? FirstScorerTeamName,
    int? TotalCards,
    int? YellowCards,
    int? RedCards,
    DateTimeOffset SubmittedUtc);
