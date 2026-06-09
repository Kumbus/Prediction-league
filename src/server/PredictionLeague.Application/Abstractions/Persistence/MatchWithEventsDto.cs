using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Read-side projection backing the admin verification page. Match + MatchEvent carry FK ids
// only; this DTO resolves Team / Player / EventType names via explicit joins.
public record MatchWithEventsDto(
    Guid MatchId,
    int ExternalFixtureId,
    DateTimeOffset KickoffUtc,
    MatchStatus Status,
    TeamRefDto HomeTeam,
    TeamRefDto AwayTeam,
    IReadOnlyList<MatchEventDto> Events);

public record TeamRefDto(Guid Id, string Name, int? Score);

public record MatchEventDto(
    int Minute,
    int? MinuteExtra,
    string Code,
    MatchEventCategory Category,
    string PlayerName,
    string TeamName);
