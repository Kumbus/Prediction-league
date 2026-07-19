using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Read-only lookup of nationalities for client dropdowns and CSV resolution.
public interface INationalityRepository : IRepository<Nationality>
{
    Task<IReadOnlyList<Nationality>> ListAsync(CancellationToken cancellationToken = default);

    // Case-insensitive lookup by ISO 3166-1 alpha-3 code (CSV import resolves rows by code).
    Task<Nationality?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}
