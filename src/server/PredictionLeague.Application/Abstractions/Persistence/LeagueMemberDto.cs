using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Read-side projection backing the league roster (FR-007). LeagueMembership.UserId is a bare Guid
// with no FK to AspNetUsers, so the display name is resolved by an explicit join in Infrastructure
// rather than by a navigation property — the Api layer never sees an Identity type.
public record LeagueMemberDto(
    Guid UserId,
    string DisplayName,
    MembershipRole Role,
    DateTimeOffset JoinedUtc);
