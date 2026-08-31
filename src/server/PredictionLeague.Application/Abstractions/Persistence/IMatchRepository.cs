using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for Match. Ingest upserts by external fixture id.
public interface IMatchRepository : IRepository<Match>
{
    // Looks the fixture up by its API id, including its Events (delete-and-replace target).
    Task<Match?> GetByExternalFixtureIdAsync(int externalFixtureId, CancellationToken cancellationToken = default);

    // One match plus its Events, by primary key. Backs scoring, which needs the result and the
    // goal/card facts in the same read. Tracked, matching GetByExternalFixtureIdAsync — the event
    // replace writes through the same graph.
    Task<Match?> GetWithEventsAsync(Guid matchId, CancellationToken cancellationToken = default);

    // The match's events with ids and resolved names, ordered as they happened. Backs the admin
    // goal/card editor, which needs the ids its selects bind to.
    Task<IReadOnlyList<MatchEventEditDto>> ListEventsForEditAsync(Guid matchId, CancellationToken cancellationToken = default);

    // Replaces a match's whole event set — Clear()-then-add on the tracked collection. Both writers
    // (the admin editor and ingest) route through this, so orphan deletion and change-tracking
    // behave identically for each. Throws InvalidOperationException when the match does not exist;
    // the caller checks first.
    //
    // Saving is the caller's call — a deliberate exception to this layer's rule that an
    // intent-named write owns its SaveChangesAsync (JoinAsync, ReplaceScoringRulesAsync,
    // UpsertManyAsync, SetAwardedPointsAsync all do). The exception exists because ingest replaces
    // a fixture's events *and* writes the fixture itself, then commits both in the one
    // save-per-match that keeps a partial run consistent; saving here would split that in two.
    Task ReplaceEventsAsync(Guid matchId, IReadOnlyList<MatchEvent> events, CancellationToken cancellationToken = default);

    // The same replace against a Match the caller already holds tracked. Ingest needs this: for a
    // fixture it just AddAsync'd, the id-based overload's query would go to the database and not
    // find the still-unsaved row. Sharing this one body is what keeps the two writers' tracking
    // semantics identical — a second hand-rolled Clear()-then-add is how the IsKeySet bug survived
    // its first fix (lessons.md).
    void ReplaceEvents(Match match, IReadOnlyList<MatchEvent> events);

    // Read-side projection for the admin tournament-detail page (resolves team + player +
    // event-type names without nav properties on the entities).
    Task<IReadOnlyList<MatchWithEventsDto>> ListByTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // Read-side projection backing the predictions screen: every match in the tournament with its
    // Round and no events, ordered by kickoff then id (stable when two kick off together). One
    // read serves both the round view and the batch write's lock check, so both compare against
    // the same KickoffUtc.
    Task<IReadOnlyList<MatchRoundDto>> ListForPredictionsAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // True when at least one match in the tournament has kicked off by asOf. The clock is a
    // parameter, not an internal UtcNow, so the caller owns "now" (S-04 scoring lock).
    Task<bool> AnyKickedOffAsync(Guid tournamentId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
