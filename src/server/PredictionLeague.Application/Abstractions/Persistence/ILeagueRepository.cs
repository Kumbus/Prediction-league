using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Application.Abstractions.Persistence;

// Per-aggregate repository for League. Add league-specific queries here as slices need them;
// S-03 added the caller-scoped list, the detail read, and the invite-code existence probe, and
// S-05 added the read-by-code plus the membership writes.
public interface ILeagueRepository : IRepository<League>
{
    // True if any League row references the given tournament. Used by tournament delete to
    // refuse cascading away leagues.
    Task<bool> AnyForTournamentAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // Leagues the user organizes or is a member of, ordered by name (FR-006).
    Task<IReadOnlyList<League>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    // One league with its scoring config and memberships. Not user-scoped — the caller decides
    // whether the requester may see it.
    Task<League?> GetWithDetailAsync(Guid leagueId, CancellationToken cancellationToken = default);

    // Every league bound to the tournament, with its ScoringRules. Backs scoring: one match's
    // result is scored once per league on its tournament, each against that league's own rules.
    // Untracked — the scoring path reads rules as values and never writes through this graph.
    Task<IReadOnlyList<League>> ListByTournamentWithRulesAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    // Write-side counterpart of GetWithDetailAsync: the same graph, but *tracked*, so the rule-set
    // replace can mutate it. GetWithDetailAsync is AsNoTracking and must not back a write.
    Task<League?> GetForUpdateAsync(Guid leagueId, CancellationToken cancellationToken = default);

    // Reconciles the tracked league's scoring config to exactly `rules` and saves once. Takes a
    // League returned by GetForUpdateAsync; `rules` are read as values (Parameter + Points) and
    // are never attached — a delete-then-add of the same (LeagueId, Parameter) would trip the
    // unique index within a single save.
    Task ReplaceScoringRulesAsync(League league, IReadOnlyList<ScoringRule> rules, CancellationToken cancellationToken = default);

    // Pre-insert probe for the invite-code generator. The unique index is the real guarantee.
    Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken cancellationToken = default);

    // The join path's entry point: one league by its invite code, *tracked*, with both ScoringRules
    // and Memberships loaded — the join both writes a membership and returns the full detail, and
    // lazy loading is off, so a missing Include would ship an empty rule set silently.
    Task<League?> GetByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default);

    // Adds the user to the league as a Member and saves. Returns normally when the user is already
    // a member — whether the pre-check or the unique index caught it — so a re-clicked invite link
    // is idempotent rather than an error. Takes a tracked League from GetByInviteCodeAsync.
    Task JoinAsync(League league, Guid userId, CancellationToken cancellationToken = default);

    // Removes the caller's membership and saves; returns normally when no row exists. When that row
    // is the league's *only* membership the league itself is removed instead, so an empty league is
    // never left behind — its scoring rules and membership go out on the configured cascade. Takes
    // a tracked League from GetForUpdateAsync.
    Task LeaveAsync(League league, Guid userId, CancellationToken cancellationToken = default);

    // Hands the league to another member in one save: League.OrganizerUserId, the outgoing
    // organizer's role, and the incoming organizer's role all move together, so the two
    // representations of "who organizes this" can never disagree. Throws InvalidOperationException
    // when the target has no membership row — the caller validates first — and
    // LeagueModifiedException when a concurrent transfer already moved the league, which would
    // otherwise let a stale snapshot write the two representations out of step.
    Task TransferOrganizerAsync(League league, Guid newOrganizerUserId, CancellationToken cancellationToken = default);

    // The league's roster with display names, oldest member first. Resolves names by an explicit
    // join — a membership whose user row is gone is skipped rather than surfaced nameless.
    Task<IReadOnlyList<LeagueMemberDto>> ListMembersAsync(Guid leagueId, CancellationToken cancellationToken = default);

    // Persists a new league with its scoring rules and memberships as one unit. Owns the
    // invite-code collision retry: on a unique-index rejection it re-codes the still-tracked league
    // from `nextCode` and saves once more, throwing InviteCodeCollisionException only after that
    // budget is spent. The generator arrives as a delegate rather than a constructor dependency —
    // RandomInviteCodeGenerator depends on this repository, so an injected IInviteCodeGenerator
    // would close a DI cycle that throws at resolve time.
    Task CreateAsync(League league, Func<CancellationToken, Task<string>> nextCode, CancellationToken cancellationToken = default);
}
