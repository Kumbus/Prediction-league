using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Leagues;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Api.Controllers;

// League create/list/detail, organizer-only scoring-rule editing, and the membership writes —
// join by invite code, leave, transfer the organizer role (FR-006, FR-007, FR-008, US-01).
// Roles are per-league via LeagueMembership, not a global policy, so visibility is checked
// inline: a caller who is neither organizer nor member gets 404, mirroring the draft-tournament
// rule in TournamentsController (no information leak).
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LeaguesController : ControllerBase
{
    private const int MaxNameLength = 200;
    private const int MinPointsPerRule = 1;
    private const int MaxPointsPerRule = 1000;

    private readonly ILeagueRepository _leagues;
    private readonly ITournamentRepository _tournaments;
    private readonly IInviteCodeGenerator _inviteCodes;
    private readonly IMatchRepository _matches;

    public LeaguesController(
        ILeagueRepository leagues,
        ITournamentRepository tournaments,
        IInviteCodeGenerator inviteCodes,
        IMatchRepository matches)
    {
        _leagues = leagues;
        _tournaments = tournaments;
        _inviteCodes = inviteCodes;
        _matches = matches;
    }

    public record ScoringRuleDto(ScoringParameter Parameter, int Points);

    public record CreateLeagueRequest(
        string Name,
        Guid TournamentId,
        IReadOnlyList<ScoringRuleDto> ScoringRules);

    public record UpdateScoringRulesRequest(IReadOnlyList<ScoringRuleDto> ScoringRules);

    public record JoinLeagueRequest(string InviteCode);

    public record TransferOrganizerRequest(Guid UserId);

    public record LeagueMemberResponse(
        Guid UserId,
        string DisplayName,
        MembershipRole Role,
        DateTimeOffset JoinedUtc);

    public record LeagueSummaryResponse(
        Guid Id,
        string Name,
        Guid TournamentId,
        string TournamentName,
        bool IsOrganizer,
        int MemberCount);

    public record LeagueDetailResponse(
        Guid Id,
        string Name,
        Guid TournamentId,
        string TournamentName,
        string InviteCode,
        bool IsOrganizer,
        // Scoring is frozen once the tournament's first match has kicked off, so the client can
        // hide the edit affordance instead of discovering the rule via a failed request.
        bool IsScoringLocked,
        // MemberCount stays alongside Members: the list page renders the count without fetching a
        // roster, so it is not redundant with the list below.
        int MemberCount,
        IReadOnlyList<ScoringRuleDto> ScoringRules,
        // The roster (FR-007). Visible to every member of the league — the same audience that can
        // already see the invite code.
        IReadOnlyList<LeagueMemberResponse> Members);

    // GET api/leagues — leagues the caller organizes or belongs to.
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var leagues = await _leagues.ListForUserAsync(userId, cancellationToken);
        if (leagues.Count == 0) return Ok(Array.Empty<LeagueSummaryResponse>());

        // One lookup for every tournament name rather than a read per league.
        var tournamentNames = (await _tournaments.ListAsync(includeUnpublished: true, cancellationToken))
            .ToDictionary(t => t.Id, t => t.Name);

        return Ok(leagues.Select(l => new LeagueSummaryResponse(
            l.Id,
            l.Name,
            l.TournamentId,
            tournamentNames.GetValueOrDefault(l.TournamentId, string.Empty),
            l.OrganizerUserId == userId,
            l.Memberships.Count)));
    }

    // GET api/leagues/{id} — 404 both when the league is missing and when the caller is neither
    // organizer nor member.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await _leagues.GetWithDetailAsync(id, cancellationToken);
        if (league is null) return NotFound();
        if (league.OrganizerUserId != userId && league.Memberships.All(m => m.UserId != userId))
            return NotFound();

        var tournament = await _tournaments.GetByIdAsync(league.TournamentId, cancellationToken);
        var isScoringLocked = await IsScoringLockedAsync(league.TournamentId, cancellationToken);
        var members = await _leagues.ListMembersAsync(league.Id, cancellationToken);
        return Ok(ToDetailResponse(league, tournament?.Name ?? string.Empty, userId, isScoringLocked, members));
    }

    // POST api/leagues — the league, its selected scoring rules, and the organizer's membership are
    // one transactional unit: a single SaveChangesAsync, so a failure can never leave a league
    // without its rules or its organizer.
    [HttpPost]
    public async Task<IActionResult> Create(CreateLeagueRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Problem(detail: "Name is required.", statusCode: StatusCodes.Status400BadRequest);
        if (name.Length > MaxNameLength)
            return Problem(detail: $"Name must be {MaxNameLength} characters or fewer.", statusCode: StatusCodes.Status400BadRequest);

        // Publishing is what makes a tournament leaguable — admins get the same rule. Missing and
        // unpublished share one message so a draft's existence does not leak, mirroring the
        // 404-masking rule at TournamentsController.cs:79.
        var tournament = await _tournaments.GetByIdAsync(request.TournamentId, cancellationToken);
        if (tournament is null || !tournament.IsPublished)
            return Problem(
                detail: "Tournament not found or not published.",
                statusCode: StatusCodes.Status400BadRequest);

        var rulesValidation = ValidateScoringRules(request.ScoringRules);
        if (rulesValidation is not null) return rulesValidation;

        string inviteCode;
        try
        {
            inviteCode = await _inviteCodes.GenerateAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Generator exhausted its retry budget — better a 503 than an unbounded loop.
            return Problem(
                detail: "Could not allocate an invite code. Please try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var league = new League
        {
            Id = Guid.NewGuid(),
            Name = name,
            TournamentId = tournament.Id,
            OrganizerUserId = userId,
            InviteCode = inviteCode,
            ScoringRules = request.ScoringRules
                .Select(r => new ScoringRule { Id = Guid.NewGuid(), Parameter = r.Parameter, Points = r.Points })
                .ToList(),
            Memberships =
            [
                new LeagueMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Role = MembershipRole.Organizer,
                    JoinedUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        try
        {
            // The repository owns the collision retry — it is the layer that can tell a rejected
            // invite code from any other write failure. The generator goes in as a delegate rather
            // than a repository dependency; injecting it would close a DI cycle.
            await _leagues.CreateAsync(league, _inviteCodes.GenerateAsync, cancellationToken);
        }
        catch (InviteCodeCollisionException)
        {
            return Problem(
                detail: "Could not create the league right now. Please try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return CreatedAtAction(
            nameof(Get),
            new { id = league.Id },
            ToDetailResponse(
                league,
                tournament.Name,
                userId,
                await IsScoringLockedAsync(league.TournamentId, cancellationToken),
                await _leagues.ListMembersAsync(league.Id, cancellationToken)));
    }

    // PUT api/leagues/{id}/scoring-rules — the organizer replaces the league's scoring config
    // (FR-008). Returns the refreshed detail so the client re-renders from the server's own view.
    // 404 masks a league the caller cannot see at all; a member who *can* see it gets a plain 403,
    // because masking a legitimate visibility as "not found" would be a lie.
    [HttpPut("{id:guid}/scoring-rules")]
    public async Task<IActionResult> UpdateScoringRules(
        Guid id,
        UpdateScoringRulesRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await _leagues.GetForUpdateAsync(id, cancellationToken);
        if (league is null) return NotFound();

        var isOrganizer = league.OrganizerUserId == userId;
        if (!isOrganizer && league.Memberships.All(m => m.UserId != userId))
            return NotFound();
        if (!isOrganizer)
            return Problem(
                detail: "Only the league organizer can change the scoring rules.",
                statusCode: StatusCodes.Status403Forbidden);

        // A state conflict, not malformed input — retuning points against known results is what
        // the freeze exists to prevent.
        var isScoringLocked = await IsScoringLockedAsync(league.TournamentId, cancellationToken);
        if (isScoringLocked)
            return Problem(
                detail: "Scoring rules are locked once the tournament has started.",
                statusCode: StatusCodes.Status409Conflict);

        var rulesValidation = ValidateScoringRules(request.ScoringRules);
        if (rulesValidation is not null) return rulesValidation;

        var rules = request.ScoringRules
            .Select(r => new ScoringRule { Parameter = r.Parameter, Points = r.Points })
            .ToList();

        await _leagues.ReplaceScoringRulesAsync(league, rules, cancellationToken);

        var tournament = await _tournaments.GetByIdAsync(league.TournamentId, cancellationToken);
        var members = await _leagues.ListMembersAsync(league.Id, cancellationToken);
        return Ok(ToDetailResponse(league, tournament?.Name ?? string.Empty, userId, isScoringLocked, members));
    }

    // POST api/leagues/join — turn an invite code into membership (FR-007). Joining twice is a
    // no-op that still returns the league: a re-clicked link from a chat thread is a normal event,
    // not an error, and the organizer using their own code is just that same case. Returns the full
    // detail so the client can render the league without a second round trip.
    [HttpPost("join")]
    public async Task<IActionResult> Join(JoinLeagueRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        // Normalized here rather than left to SQL Server's case-insensitive default collation, so
        // "a lowercase code works" is a decision this endpoint makes, not a property of the
        // database it happens to run on.
        var inviteCode = request.InviteCode?.Trim().ToUpperInvariant() ?? string.Empty;

        // An unknown code and a blank one share one message with a league the caller cannot see —
        // the same masking rule the detail route applies.
        var league = inviteCode.Length == 0
            ? null
            : await _leagues.GetByInviteCodeAsync(inviteCode, cancellationToken);
        if (league is null)
            return Problem(
                detail: "No league found for that invite code.",
                statusCode: StatusCodes.Status404NotFound);

        await _leagues.JoinAsync(league, userId, cancellationToken);

        var tournament = await _tournaments.GetByIdAsync(league.TournamentId, cancellationToken);
        var isScoringLocked = await IsScoringLockedAsync(league.TournamentId, cancellationToken);
        var members = await _leagues.ListMembersAsync(league.Id, cancellationToken);
        return Ok(ToDetailResponse(league, tournament?.Name ?? string.Empty, userId, isScoringLocked, members));
    }

    // DELETE api/leagues/{id}/membership — leave a league. The organizer must hand the league over
    // first, *unless* they are the only member left: that case deletes the league, so creating one
    // by mistake is not permanently undoable. It is the only path that destroys a league, and the
    // 409 above it means it can never destroy one other people are in.
    [HttpDelete("{id:guid}/membership")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await _leagues.GetForUpdateAsync(id, cancellationToken);
        if (league is null) return NotFound();

        var isOrganizer = league.OrganizerUserId == userId;
        if (!isOrganizer && league.Memberships.All(m => m.UserId != userId))
            return NotFound();

        if (isOrganizer && league.Memberships.Count > 1)
            return Problem(
                detail: "Transfer the league to another member before leaving.",
                statusCode: StatusCodes.Status409Conflict);

        await _leagues.LeaveAsync(league, userId, cancellationToken);
        return NoContent();
    }

    // PUT api/leagues/{id}/organizer — hand the league to another member, which is also the
    // precondition for the outgoing organizer's own exit. Returns the refreshed detail, where
    // isOrganizer is now false for the caller, so the client flips its view from the server's
    // answer rather than guessing locally.
    [HttpPut("{id:guid}/organizer")]
    public async Task<IActionResult> TransferOrganizer(
        Guid id,
        TransferOrganizerRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();

        var league = await _leagues.GetForUpdateAsync(id, cancellationToken);
        if (league is null) return NotFound();

        var isOrganizer = league.OrganizerUserId == userId;
        if (!isOrganizer && league.Memberships.All(m => m.UserId != userId))
            return NotFound();
        if (!isOrganizer)
            return Problem(
                detail: "Only the league organizer can transfer the league.",
                statusCode: StatusCodes.Status403Forbidden);

        if (request.UserId == userId)
            return Problem(
                detail: "You are already the organizer of this league.",
                statusCode: StatusCodes.Status400BadRequest);
        if (league.Memberships.All(m => m.UserId != request.UserId))
            return Problem(
                detail: "That user is not a member of this league.",
                statusCode: StatusCodes.Status400BadRequest);

        try
        {
            await _leagues.TransferOrganizerAsync(league, request.UserId, cancellationToken);
        }
        catch (LeagueModifiedException)
        {
            // Someone else transferred the league first. A state conflict, not bad input — the
            // caller's view of who organizes this league is simply stale.
            return Problem(
                detail: "This league was changed by someone else. Reload it and try again.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var tournament = await _tournaments.GetByIdAsync(league.TournamentId, cancellationToken);
        var isScoringLocked = await IsScoringLockedAsync(league.TournamentId, cancellationToken);
        var members = await _leagues.ListMembersAsync(league.Id, cancellationToken);
        return Ok(ToDetailResponse(league, tournament?.Name ?? string.Empty, userId, isScoringLocked, members));
    }

    // The rule set is *selectable*: a league scores only the parameters it lists. The invariant
    // guarded here is non-empty and distinct — not complete. A parameter that does not score is
    // left out; Points = 0 is no longer a way to say it, so the floor is 1. Shared by Create and
    // UpdateScoringRules so the two routes cannot drift.
    private IActionResult? ValidateScoringRules(IReadOnlyList<ScoringRuleDto>? rules)
    {
        if (rules is null || rules.Count == 0)
            return Problem(detail: "At least one scoring rule is required.", statusCode: StatusCodes.Status400BadRequest);

        foreach (var rule in rules)
        {
            if (!Enum.IsDefined(rule.Parameter))
                return Problem(detail: $"Unknown scoring parameter '{rule.Parameter}'.", statusCode: StatusCodes.Status400BadRequest);
            if (rule.Points < MinPointsPerRule || rule.Points > MaxPointsPerRule)
                return Problem(detail: $"Points must be between {MinPointsPerRule} and {MaxPointsPerRule}.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (rules.Select(r => r.Parameter).Distinct().Count() != rules.Count)
            return Problem(detail: "Each scoring parameter may appear only once.", statusCode: StatusCodes.Status400BadRequest);

        return null;
    }

    // The lock is derived, never stored: a league's scoring is frozen once any match in its
    // tournament has kicked off. Nothing to migrate, nothing to keep in sync — and a league
    // created after a tournament has begun is locked from birth.
    private async Task<bool> IsScoringLockedAsync(Guid tournamentId, CancellationToken cancellationToken)
        => await _matches.AnyKickedOffAsync(tournamentId, DateTimeOffset.UtcNow, cancellationToken);

    // isScoringLocked and members are passed in rather than derived here — the shaper has no
    // repository access, and every caller already knows the tournament it just read. The member
    // list in particular has to be read *after* the caller's own save: the tracked graph carries no
    // display names, and on create/join the caller's own row does not exist until the save lands.
    private static LeagueDetailResponse ToDetailResponse(
        League league,
        string tournamentName,
        Guid userId,
        bool isScoringLocked,
        IReadOnlyList<LeagueMemberDto> members)
        => new(
            league.Id,
            league.Name,
            league.TournamentId,
            tournamentName,
            league.InviteCode,
            league.OrganizerUserId == userId,
            isScoringLocked,
            league.Memberships.Count,
            league.ScoringRules
                .OrderBy(r => r.Parameter)
                .Select(r => new ScoringRuleDto(r.Parameter, r.Points))
                .ToList(),
            members
                .Select(m => new LeagueMemberResponse(m.UserId, m.DisplayName, m.Role, m.JoinedUtc))
                .ToList());

    // Identity user keys are Guids (F-01), so the NameIdentifier claim parses directly —
    // no UserManager round-trip.
    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
