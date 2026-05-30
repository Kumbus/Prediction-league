using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for League. Add league-specific queries here as slices need them
// (e.g. GetByInviteCodeAsync for S-03); none required by F-01.
public interface ILeagueRepository : IRepository<League>
{
}
