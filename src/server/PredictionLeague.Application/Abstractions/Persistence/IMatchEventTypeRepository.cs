using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for the MatchEventType dictionary. Ingest maps event detail
// to a dictionary row by its Code. GetAllAsync is inherited.
public interface IMatchEventTypeRepository : IRepository<MatchEventType>
{
    Task<MatchEventType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}
