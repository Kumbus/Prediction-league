namespace PredictionLeague.Application.Abstractions.Persistence;

// A player who may be forecast as a match's first scorer. TeamId is the team the player *belongs
// to* — the client groups the picker by it. It is not the credited team, which the member chooses
// separately so an own goal (team A credited, team B player) is expressible.
public record EligibleScorerDto(Guid PlayerId, string Name, Guid TeamId);
