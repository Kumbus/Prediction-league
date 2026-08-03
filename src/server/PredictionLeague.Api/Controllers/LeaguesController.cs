using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Leagues;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Api.Controllers;

// League create/list/detail plus organizer-only scoring-rule editing (FR-006, FR-008, US-01).
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
        int MemberCount,
        IReadOnlyList<ScoringRuleDto> ScoringRules);

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
        return Ok(ToDetailResponse(league, tournament?.Name ?? string.Empty, userId, isScoringLocked));
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
                new LeagueMembership { Id = Guid.NewGuid(), UserId = userId, Role = MembershipRole.Organizer }
            ]
        };

        try
        {
            await _leagues.CreateAsync(league, cancellationToken);
        }
        catch (InviteCodeCollisionException)
        {
            // The generator's pre-check is racy by construction — the unique index on InviteCode
            // is the real guarantee. Re-code the still-tracked league and save once more; the
            // insert is retried whole. A second collision is not worth chasing.
            try
            {
                league.InviteCode = await _inviteCodes.GenerateAsync(cancellationToken);
                await _leagues.CreateAsync(league, cancellationToken);
            }
            catch (Exception ex) when (ex is InviteCodeCollisionException or InvalidOperationException)
            {
                return Problem(
                    detail: "Could not create the league right now. Please try again.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        return CreatedAtAction(
            nameof(Get),
            new { id = league.Id },
            ToDetailResponse(
                league,
                tournament.Name,
                userId,
                await IsScoringLockedAsync(league.TournamentId, cancellationToken)));
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
        return Ok(ToDetailResponse(league, tournament?.Name ?? string.Empty, userId, isScoringLocked));
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

    // isScoringLocked is passed in rather than derived here — the shaper has no repository access,
    // and every caller already knows the tournament it just read.
    private static LeagueDetailResponse ToDetailResponse(League league, string tournamentName, Guid userId, bool isScoringLocked)
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
                .ToList());

    // Identity user keys are Guids (F-01), so the NameIdentifier claim parses directly —
    // no UserManager round-trip.
    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
