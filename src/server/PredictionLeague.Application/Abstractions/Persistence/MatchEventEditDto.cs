namespace PredictionLeague.Application.Abstractions.Persistence;

// Read-side projection backing the admin goal/card editor (FR-005). Carries the ids the form binds
// its selects to *and* the resolved names it renders — MatchEvent holds FK ids only.
//
// Deliberately not a widening of MatchEventDto (MatchWithEventsDto.cs): that one carries names but
// no ids and backs the admin tournament-detail projection, so widening it would drag that read into
// this slice for no gain.
public record MatchEventEditDto(
    Guid Id,
    int MatchEventTypeId,
    string TypeCode,
    string TypeDisplayName,
    Guid PlayerId,
    string PlayerName,
    Guid TeamId,
    string TeamName,
    int Minute,
    int? MinuteExtra);
