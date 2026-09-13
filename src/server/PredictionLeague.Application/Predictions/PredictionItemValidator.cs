using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Predictions;

// Whether one submitted item is a forecast this league can store, and what its scores are. A pure
// function over the item, the match it names, the parameters the league scores and the players
// eligible to be its first scorer — no clock, no database, no HTTP.
//
// It deliberately does NOT decide whether the match is still open: that is a fact about the match
// and the current time, judged once per batch against a single clock, and is the caller's job.
// This answers only "is this a forecast, and is it one this league asked for".
//
// The rule it exists to keep in one place: a field the league does not score is refused outright
// rather than dropped, because a member must never believe they forecast something the league
// will never award.
public static class PredictionItemValidator
{
    public const int MaxScore = 99;
    public const int MaxCards = 99;

    public static PredictionItemValidation Validate(
        PredictionItemInput item,
        MatchRoundDto match,
        IReadOnlySet<ScoringParameter> scored,
        IReadOnlyList<EligibleScorerDto> eligible)
    {
        // Both scores are what makes an item a forecast at all — every other field is a
        // league-dependent extra. Checked first, and separately from the range check below, so the
        // member is told what is missing rather than being handed a range they did not violate.
        if (item.HomeScore is not { } homeScore || item.AwayScore is not { } awayScore)
            return PredictionItemValidation.Rejected("Both scores are required.");

        if (homeScore < 0 || homeScore > MaxScore || awayScore < 0 || awayScore > MaxScore)
            return PredictionItemValidation.Rejected($"Scores must be between 0 and {MaxScore}.");

        var cardError =
            ValidateCard(ScoringParameter.CorrectCardCount, item.TotalCards, "Total cards", scored)
            ?? ValidateCard(ScoringParameter.CorrectYellowCards, item.YellowCards, "Yellow cards", scored)
            ?? ValidateCard(ScoringParameter.CorrectRedCards, item.RedCards, "Red cards", scored);
        if (cardError is not null) return PredictionItemValidation.Rejected(cardError);

        var scorerError = ValidateFirstScorer(item, match, scored, eligible);
        if (scorerError is not null) return PredictionItemValidation.Rejected(scorerError);

        return PredictionItemValidation.Accepted(homeScore, awayScore);
    }

    private static string? ValidateFirstScorer(
        PredictionItemInput item,
        MatchRoundDto match,
        IReadOnlySet<ScoringParameter> scored,
        IReadOnlyList<EligibleScorerDto> eligible)
    {
        if (!scored.Contains(ScoringParameter.CorrectGoalScorer))
            return item.FirstScorerPlayerId is not null || item.FirstScorerTeamId is not null
                ? "This league does not score the first goal scorer."
                : null;

        // Optional even where the league scores it: a member who leaves the scorer blank simply
        // cannot earn those points. Requiring it would also dead-end every league whose teams have
        // no linked players — the candidate list would be empty with no way to satisfy the rule.
        if (item.FirstScorerPlayerId is null && item.FirstScorerTeamId is null)
            return null;

        // Half a pair is not a forecast, though: a player with no credited team cannot be scored,
        // and a credited team with no player says nothing.
        if (item.FirstScorerPlayerId is null || item.FirstScorerTeamId is null)
            return "Pick both a first scorer and the team the goal is credited to.";

        if (item.FirstScorerTeamId != match.HomeTeam.Id && item.FirstScorerTeamId != match.AwayTeam.Id)
            return "The credited team must be one of the two teams playing.";

        if (eligible.All(s => s.PlayerId != item.FirstScorerPlayerId))
            return "That player is not in either team's squad for this match.";

        // Deliberately not required to agree: a player from one team credited to the other is an
        // own-goal forecast, which is exactly the shape MatchEvent records.
        return null;
    }

    // A field the league does not score is refused outright rather than dropped. A field it *does*
    // score is optional, on the same footing as the first scorer: leaving it blank forfeits those
    // points and nothing else, so an unfilled row still saves its scores.
    private static string? ValidateCard(
        ScoringParameter parameter,
        int? value,
        string label,
        IReadOnlySet<ScoringParameter> scored)
    {
        if (!scored.Contains(parameter))
            return value is null ? null : $"This league does not score {label.ToLowerInvariant()}.";
        if (value is null) return null;
        return value < 0 || value > MaxCards ? $"{label} must be between 0 and {MaxCards}." : null;
    }
}

// The verdict, carrying the scores when there is one. They travel with the result rather than
// being re-read from the input because the caller would otherwise have to unwrap the same two
// nullables the validator already proved present — and the compiler would not believe it.
public readonly record struct PredictionItemValidation(string? Error, int HomeScore, int AwayScore)
{
    public bool IsValid => Error is null;

    // Scores are meaningless on a rejection; IsValid is what guards reading them.
    public static PredictionItemValidation Rejected(string reason) => new(reason, 0, 0);

    public static PredictionItemValidation Accepted(int homeScore, int awayScore)
        => new(null, homeScore, awayScore);
}
