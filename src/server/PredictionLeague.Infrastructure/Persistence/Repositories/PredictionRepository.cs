using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Predictions;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Infrastructure.Persistence.Repositories;

// EF Core Prediction repository. Owns the round-batch upsert, including the unique-index
// collision two concurrent first-time saves can produce — translated here rather than in the
// controller, which must not know EF Core exists (lessons.md:25).
public class PredictionRepository : BaseRepository<Prediction>, IPredictionRepository
{
    public PredictionRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Prediction>> ListForUserAsync(
        Guid leagueId,
        Guid userId,
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken = default)
    {
        if (matchIds.Count == 0) return [];

        var ids = matchIds.ToList();
        return await Set
            .AsNoTracking()
            .Where(p => p.LeagueId == leagueId && p.UserId == userId && ids.Contains(p.MatchId))
            .ToListAsync(cancellationToken);
    }

    // Left joins on the scorer pair: both halves are nullable, and an inner join would drop every
    // score-only forecast. The display-name join is inner by construction — a forecast whose user
    // row is gone drops out rather than showing up nameless, matching ListMembersAsync.
    public async Task<IReadOnlyList<MemberPredictionDto>> ListForMatchesAsync(
        Guid leagueId,
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken = default)
    {
        if (matchIds.Count == 0) return [];

        var ids = matchIds.ToList();

        var query =
            from p in Set.AsNoTracking().Where(p => p.LeagueId == leagueId && ids.Contains(p.MatchId))
            join u in Context.Users.AsNoTracking() on p.UserId equals u.Id
            join pl in Context.Players.AsNoTracking() on p.PredictedFirstScorerPlayerId equals pl.Id into scorers
            from scorer in scorers.DefaultIfEmpty()
            join t in Context.Teams.AsNoTracking() on p.PredictedFirstScorerTeamId equals t.Id into creditedTeams
            from creditedTeam in creditedTeams.DefaultIfEmpty()
            orderby u.DisplayName
            select new MemberPredictionDto(
                p.MatchId,
                p.UserId,
                u.DisplayName,
                p.PredictedHomeScore,
                p.PredictedAwayScore,
                p.PredictedFirstScorerPlayerId,
                scorer == null ? null : scorer.Name,
                p.PredictedFirstScorerTeamId,
                creditedTeam == null ? null : creditedTeam.Name,
                p.PredictedTotalCards,
                p.PredictedYellowCards,
                p.PredictedRedCards,
                p.SubmittedUtc);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Prediction>> ListForMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default)
        => await Set
            .AsNoTracking()
            .Where(p => p.MatchId == matchId)
            .ToListAsync(cancellationToken);

    // Loads the match's predictions *tracked* and applies the computed points, so the untracked
    // read the scorer worked from never has to be attached. Ids absent from the map are left
    // alone; ids in the map that no longer belong to this match are ignored — a prediction deleted
    // between the read and this write must not resurrect as an update.
    public async Task SetAwardedPointsAsync(
        Guid matchId,
        IReadOnlyDictionary<Guid, int?> pointsByPredictionId,
        CancellationToken cancellationToken = default)
    {
        if (pointsByPredictionId.Count == 0) return;

        var rows = await Set
            .Where(p => p.MatchId == matchId)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var row in rows)
        {
            if (!pointsByPredictionId.TryGetValue(row.Id, out var points)) continue;
            if (row.AwardedPoints == points) continue;

            row.AwardedPoints = points;
            changed = true;
        }

        // One save for the whole match, so a match is never half-scored. Skipped entirely when a
        // re-run computed exactly what is already stored — the idempotence the triggers rely on.
        if (changed)
            await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertManyAsync(
        Guid leagueId,
        Guid userId,
        IReadOnlyList<Prediction> predictions,
        CancellationToken cancellationToken = default)
    {
        if (predictions.Count == 0) return;

        var matchIds = predictions.Select(p => p.MatchId).ToList();
        var existing = await LoadTrackedAsync(leagueId, userId, matchIds, cancellationToken);

        var inserted = new List<Prediction>();
        foreach (var incoming in predictions)
        {
            if (existing.TryGetValue(incoming.MatchId, out var row))
            {
                Apply(row, incoming);
            }
            else
            {
                await Set.AddAsync(incoming, cancellationToken);
                inserted.Add(incoming);
            }
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (DbUpdateException ex) when (IsPredictionCollision(ex))
        {
            // A concurrent save of the same round — a double-clicked button, two tabs — inserted
            // first. The caller asked for their forecast to be stored, and it is a *write*, so the
            // right answer is to apply it over the winner's row rather than fail: detach the
            // phantom inserts, re-read what actually landed, and update once.
            foreach (var phantom in inserted)
                Context.Entry(phantom).State = EntityState.Detached;
        }

        var reread = await LoadTrackedAsync(leagueId, userId, matchIds, cancellationToken);
        foreach (var incoming in predictions)
        {
            if (reread.TryGetValue(incoming.MatchId, out var row))
                Apply(row, incoming);
            else
                // Not the row that collided — something else in the batch is genuinely new (the
                // loser of the race saved a different subset). Insert it on the retry.
                await Set.AddAsync(incoming, cancellationToken);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPredictionCollision(ex))
        {
            // Lost the race twice — a third concurrent first-time save landed between the re-read
            // and this write. Retrying again would be a loop with no bound, so translate it into a
            // domain conflict the caller can answer with a 409 and let the member re-submit.
            throw new PredictionConflictException(leagueId, ex);
        }
    }

    private async Task<Dictionary<Guid, Prediction>> LoadTrackedAsync(
        Guid leagueId,
        Guid userId,
        List<Guid> matchIds,
        CancellationToken cancellationToken)
        => await Set
            .Where(p => p.LeagueId == leagueId && p.UserId == userId && matchIds.Contains(p.MatchId))
            .ToDictionaryAsync(p => p.MatchId, cancellationToken);

    // Overwrite semantics: every predictable field moves, including back to null when the caller
    // cleared it. A field the league does not score never reaches here — the controller rejects it.
    private static void Apply(Prediction target, Prediction incoming)
    {
        target.PredictedHomeScore = incoming.PredictedHomeScore;
        target.PredictedAwayScore = incoming.PredictedAwayScore;
        target.PredictedFirstScorerPlayerId = incoming.PredictedFirstScorerPlayerId;
        target.PredictedFirstScorerTeamId = incoming.PredictedFirstScorerTeamId;
        target.PredictedTotalCards = incoming.PredictedTotalCards;
        target.PredictedYellowCards = incoming.PredictedYellowCards;
        target.PredictedRedCards = incoming.PredictedRedCards;
        target.SubmittedUtc = incoming.SubmittedUtc;
    }

    // SQL Server reports a unique-index violation as 2601 (index) or 2627 (constraint) and names
    // the index — checked so an unrelated write failure is not mistaken for a double-submit and
    // retried pointlessly. Same shape as LeagueRepository's collision checks.
    private static bool IsPredictionCollision(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 } sql
           && sql.Message.Contains("IX_Predictions_LeagueId_UserId_MatchId", StringComparison.OrdinalIgnoreCase);
}
