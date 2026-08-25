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

    public async Task<IReadOnlyList<League>> ListByTournamentWithRulesAsync(Guid tournamentId, CancellationToken cancellationToken = default)
        => await Set
            .AsNoTracking()
            .Include(l => l.ScoringRules)
            .Where(l => l.TournamentId == tournamentId)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

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
        // A detached league fails silently and partially: Remove still deletes (it attaches the
        // child as Deleted), but the Points updates and the added rules are never observed, so
        // SaveChangesAsync returns clean on a half-written config. GetWithDetailAsync is
        // AsNoTracking and sits right above GetForUpdateAsync — make the wrong one loud.
        if (Context.Entry(league).State == EntityState.Detached)
            throw new InvalidOperationException(
                $"{nameof(ReplaceScoringRulesAsync)} requires a tracked League from {nameof(GetForUpdateAsync)}.");

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
            var rule = new ScoringRule
            {
                Id = Guid.NewGuid(),
                LeagueId = league.Id,
                Parameter = parameter,
                Points = points
            };
            league.ScoringRules.Add(rule);

            // Explicit Add for the same reason JoinAsync needs one: the league is tracked and
            // Unchanged, so EF's IsKeySet heuristic would paint this key-set child Modified and
            // UPDATE a row that was never inserted.
            Context.Set<ScoringRule>().Add(rule);
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken cancellationToken = default)
        => await Set.AnyAsync(l => l.InviteCode == inviteCode, cancellationToken);

    // Tracked, because the join path writes a membership onto what it reads. ScoringRules is not
    // optional here: the join returns the full league detail and lazy loading is off, so leaving it
    // out would ship an empty rule set rather than fail.
    public async Task<League?> GetByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default)
        => await Set
            .Include(l => l.ScoringRules)
            .Include(l => l.Memberships)
            .FirstOrDefaultAsync(l => l.InviteCode == inviteCode, cancellationToken);

    public async Task JoinAsync(League league, Guid userId, CancellationToken cancellationToken = default)
    {
        RequireTracked(league, nameof(JoinAsync), nameof(GetByInviteCodeAsync));

        if (league.Memberships.Any(m => m.UserId == userId)) return;

        var membership = new LeagueMembership
        {
            Id = Guid.NewGuid(),
            LeagueId = league.Id,
            UserId = userId,
            Role = MembershipRole.Member,
            JoinedUtc = DateTimeOffset.UtcNow
        };
        league.Memberships.Add(membership);

        // Added explicitly, not left to navigation fixup. A child discovered in a tracked parent's
        // collection is painted by EF's IsKeySet heuristic: the Id is already set, so EF reads it
        // as an existing row and marks it Modified, emitting an UPDATE against a row that does not
        // exist. The create path gets away with the same shape only because its League is Added,
        // which paints the whole graph Added.
        Context.Set<LeagueMembership>().Add(membership);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsMembershipCollision(ex))
        {
            // Two submissions of the same code by the same user both passed the check above and
            // both inserted; the unique index rejected this one. The end state is what the caller
            // asked for either way — the user is a member — so unwind the phantom row (it would
            // otherwise inflate MemberCount off the tracked graph) and return successfully.
            Context.Entry(membership).State = EntityState.Detached;
            league.Memberships.Remove(membership);
        }
    }

    public async Task LeaveAsync(League league, Guid userId, CancellationToken cancellationToken = default)
    {
        RequireTracked(league, nameof(LeaveAsync), nameof(GetForUpdateAsync));

        var membership = league.Memberships.FirstOrDefault(m => m.UserId == userId);
        if (membership is null) return;

        if (league.Memberships.Count == 1)
        {
            // The last member out takes the league with them, so a league created by mistake does
            // not outlive everyone's interest in it. The count is read off the tracked graph rather
            // than re-queried, and ScoringRules + the membership go out on the cascade configured
            // in LeagueConfiguration — still one save.
            Set.Remove(league);
        }
        else
        {
            // The cascade relationship has no inverse navigation to lean on for orphan deletion —
            // remove the child explicitly, as ReplaceScoringRulesAsync does.
            Context.Set<LeagueMembership>().Remove(membership);
            league.Memberships.Remove(membership);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone already deleted what this call is deleting — the same user leaving from two
            // tabs, most likely. EF Core compares affected-row counts on every delete, so the
            // loser sees this even without a concurrency token. The caller asked for the row to be
            // gone and it is gone, so this is success, not a conflict: the same idempotency
            // JoinAsync gives the mirror-image race.
            Context.ChangeTracker.Clear();
        }
    }

    // The organizer is represented twice — League.OrganizerUserId and a Role = Organizer membership
    // row — and no read path checks the two agree, so both move in one save or neither does.
    public async Task TransferOrganizerAsync(League league, Guid newOrganizerUserId, CancellationToken cancellationToken = default)
    {
        RequireTracked(league, nameof(TransferOrganizerAsync), nameof(GetForUpdateAsync));

        var target = league.Memberships.FirstOrDefault(m => m.UserId == newOrganizerUserId)
            ?? throw new InvalidOperationException(
                $"User {newOrganizerUserId} has no membership in league {league.Id}.");

        foreach (var organizer in league.Memberships.Where(m => m.Role == MembershipRole.Organizer))
            organizer.Role = MembershipRole.Member;

        target.Role = MembershipRole.Organizer;
        league.OrganizerUserId = newOrganizerUserId;

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another transfer committed between this call's read and its write. Its snapshot
            // demoted an organizer that is no longer the organizer, so saving anyway would leave
            // two Role = Organizer rows with OrganizerUserId naming only one. Refuse instead —
            // translated at this boundary so the caller answers 409 without naming an EF type.
            throw new LeagueModifiedException(league.Id, ex);
        }
    }

    // LeagueMembership.UserId has no FK to AspNetUsers, so the display name comes from an explicit
    // join. It is an inner join by construction: a membership whose user row is gone drops out
    // rather than showing up nameless.
    public async Task<IReadOnlyList<LeagueMemberDto>> ListMembersAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        var members = from m in Context.Set<LeagueMembership>().AsNoTracking()
                      join u in Context.Users.AsNoTracking() on m.UserId equals u.Id
                      where m.LeagueId == leagueId
                      orderby m.JoinedUtc
                      select new LeagueMemberDto(m.UserId, u.DisplayName, m.Role, m.JoinedUtc);

        return await members.ToListAsync(cancellationToken);
    }

    // One SaveChangesAsync covers the league, its scoring rules, and its memberships, so a
    // failure can never leave a league without its config or its organizer. The invite-code
    // collision retry lives here rather than in the controller: reacting to a unique-index
    // violation is a persistence concern, and the Api layer should not know write failures exist.
    //
    // The generator arrives as a delegate, not a constructor dependency. RandomInviteCodeGenerator
    // already depends on ILeagueRepository for its pre-insert probe and both are AddScoped, so an
    // injected IInviteCodeGenerator would close the cycle ILeagueRepository → IInviteCodeGenerator
    // → ILeagueRepository — a resolve-time throw that leaves the build green and 500s every league
    // route.
    public async Task CreateAsync(League league, Func<CancellationToken, Task<string>> nextCode, CancellationToken cancellationToken = default)
    {
        if (Context.Entry(league).State == EntityState.Detached)
            await Set.AddAsync(league, cancellationToken);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (DbUpdateException ex) when (IsInviteCodeCollision(ex))
        {
            // The generator's pre-check is racy by construction — the unique index on InviteCode is
            // the real guarantee. Fall through to exactly one retry; a second collision is not
            // worth chasing.
        }

        // The two failures are caught separately, and each only around the call that can raise it.
        // A single try over both would let *any* InvalidOperationException out of
        // SaveChangesAsync — a tracking or validation bug, say — be reported to the user as "could
        // not allocate an invite code", hiding the real cause behind a 503.
        try
        {
            league.InviteCode = await nextCode(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // The generator exhausted its own budget looking for a free code.
            throw new InviteCodeCollisionException(league.InviteCode, ex);
        }

        try
        {
            // A failed SaveChangesAsync leaves the graph tracked as Added, so re-coding the same
            // instance and saving again retries the insert whole.
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsInviteCodeCollision(ex))
        {
            // The retry collided too. One domain exception either way, so the caller never has to
            // name an EF Core or provider type.
            throw new InviteCodeCollisionException(league.InviteCode, ex);
        }
    }

    // SQL Server reports a unique-index violation as error 2601 (index) or 2627 (constraint) and
    // names the offending index in the message — checked so an unrelated write failure is not
    // mistaken for a code collision and retried pointlessly.
    private static bool IsInviteCodeCollision(DbUpdateException ex)
        => IsUniqueViolationOf(ex, "IX_Leagues_InviteCode");

    // Same check against the (LeagueId, UserId) index — the guarantee that makes a double join
    // idempotent rather than a duplicate row.
    private static bool IsMembershipCollision(DbUpdateException ex)
        => IsUniqueViolationOf(ex, "IX_LeagueMemberships_LeagueId_UserId");

    private static bool IsUniqueViolationOf(DbUpdateException ex, string indexName)
        => ex.InnerException is SqlException { Number: 2601 or 2627 } sql
           && sql.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase);

    // The write methods mutate the graph they are handed, so a detached league fails silently and
    // partially — GetWithDetailAsync is AsNoTracking and sits right beside GetForUpdateAsync, so
    // make the wrong one loud.
    private void RequireTracked(League league, string method, string trackedReader)
    {
        if (Context.Entry(league).State == EntityState.Detached)
            throw new InvalidOperationException(
                $"{method} requires a tracked League from {trackedReader}.");
    }
}
