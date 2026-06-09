using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

public class NationalityRepository : BaseRepository<Nationality>, INationalityRepository
{
    public NationalityRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Nationality>> ListAsync(CancellationToken cancellationToken = default)
        => await Set.OrderBy(n => n.Name).ToListAsync(cancellationToken);

    public async Task<Nationality?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(n => n.Code.ToUpper() == code.ToUpper(), cancellationToken);
}
