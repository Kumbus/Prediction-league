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
    // they predicted. Standings take the opposite stance — ListStandingsAsync below is driven by
    // the roster, so a leaver drops out of the table even though their forecasts survive here.
    Task<IReadOnlyList<MemberPredictionDto>> ListForMatchesAsync(
        Guid leagueId,
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken = default);

    // A league's table (FR-012): every *current* member with their total points, how many matches
    // they have been scored on, and how many forecasts they have made. Driven by LeagueMembership
    // left-joined to predictions, so a member who never predicted appears with zero and a member
    // who left does not appear at all. Ordered points descending, then display name.
    Task<IReadOnlyList<StandingRowDto>> ListStandingsAsync(
        Guid leagueId,
        CancellationToken cancellationToken = default);

    // Every forecast on one match, across *all* leagues — the scoring input. Untracked, matching
    // ListForUserAsync's stance: the read feeds a computation and the write half below re-reads.
    Task<IReadOnlyList<Prediction>> ListForMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default);

    // The write half of scoring, and the reason the read above stays untracked. Every other write
    // in this layer is an intent-named repository method that owns its save (UpsertManyAsync,
    // ReplaceScoringRulesAsync, JoinAsync, TransferOrganizerAsync); handing a tracked graph out to
    // a service that mutates it and calls the generic SaveChangesAsync would break that convention
    // and make "one save per match" unenforceable — anything else tracked in the same scoped
    // context would flush with it.
    //
    // A null value un-scores that prediction (the match is no longer Finished, or lost its score);
    // 0 means "scored, earned nothing". One SaveChangesAsync inside the method covers the whole
    // match, so a match is never half-scored.
    Task SetAwardedPointsAsync(
        Guid matchId,
        IReadOnlyDictionary<Guid, int?> pointsByPredictionId,
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
