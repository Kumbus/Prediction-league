using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PredictionLeague.Application.Abstractions.Leagues;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;

namespace PredictionLeague.Api.Controllers;

// League create/list/detail for the signed-in organizer (FR-006, FR-008 partial, US-01).
// Roles are per-league via LeagueMembership, not a global policy, so visibility is checked
// inline: a caller who is neither organizer nor member gets 404, mirroring the draft-tournament
// rule in TournamentsController (no information leak).
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LeaguesController : ControllerBase
{
    private const int MaxNameLength = 200;
    private const int MaxPointsPerRule = 1000;

    private readonly ILeagueRepository _leagues;
    private readonly ITournamentRepository _tournaments;
    private readonly IInviteCodeGenerator _inviteCodes;

    public LeaguesController(
        ILeagueRepository leagues,
        ITournamentRepository tournaments,
        IInviteCodeGenerator inviteCodes)
    {
        _leagues = leagues;
        _tournaments = tournaments;
        _inviteCodes = inviteCodes;
    }

    public record ScoringRuleDto(ScoringParameter Parameter, int Points);

    public record CreateLeagueRequest(
        string Name,
        Guid TournamentId,
        IReadOnlyList<ScoringRuleDto> ScoringRules);

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
        return Ok(ToDetailResponse(league, tournament?.Name ?? string.Empty, userId));
    }

    // POST api/leagues — the league, its full scoring config, and the organizer's membership are
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

        var tournament = await _tournaments.GetByIdAsync(request.TournamentId, cancellationToken);
        if (tournament is null)
            return Problem(detail: "Tournament not found.", statusCode: StatusCodes.Status400BadRequest);
        // Publishing is what makes a tournament leaguable — admins get the same rule.
        if (!tournament.IsPublished)
            return Problem(detail: "Tournament is not published.", statusCode: StatusCodes.Status400BadRequest);

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

        await _leagues.AddAsync(league, cancellationToken);

        try
        {
            await _leagues.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The generator's pre-check is racy by construction — the unique index on InviteCode
            // is the real guarantee. Re-code the still-tracked league and save once more; the
            // insert is retried whole. A second failure is not a collision worth chasing.
            try
            {
                league.InviteCode = await _inviteCodes.GenerateAsync(cancellationToken);
                await _leagues.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                return Problem(
                    detail: "Could not create the league right now. Please try again.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        return CreatedAtAction(
            nameof(Get),
            new { id = league.Id },
            ToDetailResponse(league, tournament.Name, userId));
    }

    // Every ScoringParameter member must appear exactly once — unknown or duplicated parameters
    // are rejected rather than silently dropped, so a league's config is always complete.
    private IActionResult? ValidateScoringRules(IReadOnlyList<ScoringRuleDto>? rules)
    {
        var expected = Enum.GetValues<ScoringParameter>();

        if (rules is null || rules.Count == 0)
            return Problem(detail: "Scoring rules are required.", statusCode: StatusCodes.Status400BadRequest);

        foreach (var rule in rules)
        {
            if (!Enum.IsDefined(rule.Parameter))
                return Problem(detail: $"Unknown scoring parameter '{rule.Parameter}'.", statusCode: StatusCodes.Status400BadRequest);
            // Zero is legal and means "this parameter does not score".
            if (rule.Points < 0 || rule.Points > MaxPointsPerRule)
                return Problem(detail: $"Points must be between 0 and {MaxPointsPerRule}.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (rules.Select(r => r.Parameter).Distinct().Count() != rules.Count)
            return Problem(detail: "Each scoring parameter may appear only once.", statusCode: StatusCodes.Status400BadRequest);

        var missing = expected.Except(rules.Select(r => r.Parameter)).ToList();
        if (missing.Count > 0)
            return Problem(
                detail: $"Missing scoring parameter(s): {string.Join(", ", missing)}.",
                statusCode: StatusCodes.Status400BadRequest);

        return null;
    }

    private static LeagueDetailResponse ToDetailResponse(League league, string tournamentName, Guid userId)
        => new(
            league.Id,
            league.Name,
            league.TournamentId,
            tournamentName,
            league.InviteCode,
            league.OrganizerUserId == userId,
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
