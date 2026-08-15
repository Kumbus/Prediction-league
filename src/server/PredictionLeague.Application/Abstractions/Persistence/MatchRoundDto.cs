using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Read-side projection backing the predictions screen. Deliberately separate from
// MatchWithEventsDto: that one carries no Round and hauls every match event along, and this slice
// does not change it. This is the single read behind both the round view and the batch write's
// lock check, so IsLocked compares against the same KickoffUtc on both paths.
public record MatchRoundDto(
    Guid MatchId,
    string Round,
    DateTimeOffset KickoffUtc,
    MatchStatus Status,
    TeamRefDto HomeTeam,
    TeamRefDto AwayTeam);
