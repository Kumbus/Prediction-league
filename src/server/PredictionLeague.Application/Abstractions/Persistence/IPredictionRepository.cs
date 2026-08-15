using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Prediction (FR-009). Two reads back the predictions screen — the
// caller's own round, and every member's forecasts once a match has kicked off — and one write
// saves a whole round at a time.
public interface IPredictionRepository : IRepository<Prediction>
{
    // The caller's own forecasts for a set of matches in one league. Untracked: the read path
    // renders them and the write path re-reads for update.
    Task<IReadOnlyList<Prediction>> ListForUserAsync(
        Guid leagueId,
        Guid userId,
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken = default);

    // Every member's forecasts for the given matches, with display names resolved by an explicit
    // join (UserId has no FK to AspNetUsers). The caller decides which matches may be revealed —
    // this read applies no kickoff rule of its own.
    //
    // Scoped by league, deliberately *not* by current membership: a forecast belongs to the moment
    // it was made, so someone who later leaves the league still appears in the reveal for matches
    // they predicted. S-07 standings depend on the same rule — a leaver's earned points do not
    // vanish from the table they were earned in.
    Task<IReadOnlyList<MemberPredictionDto>> ListForMatchesAsync(
        Guid leagueId,
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken = default);

    // Insert-or-update the batch in one SaveChangesAsync, so a round saves as a unit. Idempotent
    // under a racing double-submit: read-then-write alone lets two first-time saves of the same
    // round both insert, and the unique index rejects the loser — this absorbs that rejection by
    // re-reading and applying the update once. Last write wins, which is the right semantic for a
    // member overwriting their own forecast. No EF-shaped exception reaches the caller: a second
    // collision on the retry surfaces as PredictionConflictException, not DbUpdateException.
    Task UpsertManyAsync(
        Guid leagueId,
        Guid userId,
        IReadOnlyList<Prediction> predictions,
        CancellationToken cancellationToken = default);
}
