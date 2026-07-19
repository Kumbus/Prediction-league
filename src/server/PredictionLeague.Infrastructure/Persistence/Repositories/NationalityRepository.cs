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

    // Plain equality is sargable and hits IX_Nationalities_Code. The Code column uses the DB's
    // default case-insensitive collation (SQL_Latin1_General_CP1_CI_AS), so no ToUpper() is
    // needed — that was what defeated the index.
    public async Task<Nationality?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(n => n.Code == code, cancellationToken);
}
