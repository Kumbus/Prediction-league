using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Leagues;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core League repository. Inherits the generic CRUD base; league-specific queries land
// here as slices need them.
public class LeagueRepository : BaseRepository<League>, ILeagueRepository
{
    public LeagueRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<bool> AnyForTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default)
        => await Set.AnyAsync(l => l.TournamentId == tournamentId, cancellationToken);

    // Organizer OR member — the organizer also gets a membership row at create, but the OR keeps
    // the query correct even if that ever diverges.
    public async Task<IReadOnlyList<League>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Set
            .AsNoTracking()
            .Include(l => l.Memberships)
            .Where(l => l.OrganizerUserId == userId || l.Memberships.Any(m => m.UserId == userId))
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

    public async Task<League?> GetWithDetailAsync(Guid leagueId, CancellationToken cancellationToken = default)
        => await Set
            .AsNoTracking()
            .Include(l => l.ScoringRules)
            .Include(l => l.Memberships)
            .FirstOrDefaultAsync(l => l.Id == leagueId, cancellationToken);

    // Same graph as GetWithDetailAsync, tracked — the scoring-rule replace mutates what it returns.
    public async Task<League?> GetForUpdateAsync(Guid leagueId, CancellationToken cancellationToken = default)
        => await Set
            .Include(l => l.ScoringRules)
            .Include(l => l.Memberships)
            .FirstOrDefaultAsync(l => l.Id == leagueId, cancellationToken);

    // Reconciles in place rather than delete-and-reinsert: (LeagueId, Parameter) is unique and EF
    // Core does not guarantee the DELETE is batched ahead of an INSERT for the same key in one
    // SaveChangesAsync, so toggling a parameter off and on again would hit error 2601. Incoming
    // rules are read as values only and never attached.
    public async Task ReplaceScoringRulesAsync(League league, IReadOnlyList<ScoringRule> rules, CancellationToken cancellationToken = default)
    {
        var incoming = rules.ToDictionary(r => r.Parameter, r => r.Points);

        foreach (var existing in league.ScoringRules.ToList())
        {
            if (incoming.TryGetValue(existing.Parameter, out var points))
            {
                existing.Points = points;
            }
            else
            {
                // Set is DbSet<League> here, and the cascade relationship has no inverse
                // navigation to lean on for orphan deletion — remove the child explicitly.
                Context.Set<ScoringRule>().Remove(existing);
                league.ScoringRules.Remove(existing);
            }
        }

        var present = league.ScoringRules.Select(r => r.Parameter).ToHashSet();
        foreach (var (parameter, points) in incoming.Where(kv => !present.Contains(kv.Key)))
        {
            league.ScoringRules.Add(new ScoringRule
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Parameter = parameter,
                Points = points
            });
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken cancellationToken = default)
        => await Set.AnyAsync(l => l.InviteCode == inviteCode, cancellationToken);

    // One SaveChangesAsync covers the league, its scoring rules, and its memberships, so a
    // failure can never leave a league without its config or its organizer. A rejected invite
    // code surfaces as a domain exception — provider knowledge stops here.
    public async Task CreateAsync(League league, CancellationToken cancellationToken = default)
    {
        // A failed SaveChangesAsync leaves the graph tracked as Added, so a caller retrying with a
        // fresh invite code re-enters here with the same instance — only attach it the first time.
        if (Context.Entry(league).State == EntityState.Detached)
            await Set.AddAsync(league, cancellationToken);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsInviteCodeCollision(ex))
        {
            throw new InviteCodeCollisionException(league.InviteCode, ex);
        }
    }

    // SQL Server reports a unique-index violation as error 2601 (index) or 2627 (constraint) and
    // names the offending index in the message — checked so an unrelated write failure is not
    // mistaken for a code collision and retried pointlessly.
    private static bool IsInviteCodeCollision(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 } sql
           && sql.Message.Contains("IX_Leagues_InviteCode", StringComparison.OrdinalIgnoreCase);
}
