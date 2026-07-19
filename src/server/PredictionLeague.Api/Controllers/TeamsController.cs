using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// Admin-managed teams. Ingest still upserts teams by ExternalTeamId; this surface lets an admin
// create manual teams (NULL external id) so manual matches have home/away opponents to reference.
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class TeamsController : ControllerBase
{
    private readonly ITeamRepository _teams;

    public TeamsController(ITeamRepository teams)
    {
        _teams = teams;
    }

    public record TeamResponse(Guid Id, string Name, int? ExternalTeamId, string? LogoUrl);

    public record CreateTeamRequest(string Name, string? LogoUrl);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var teams = await _teams.ListAsync(cancellationToken);
        return Ok(teams.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTeamRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(detail: "Name is required.", statusCode: StatusCodes.Status400BadRequest);

        var existing = await _teams.FindByNameAsync(request.Name.Trim(), cancellationToken);
        if (existing is not null)
            return Problem(detail: "A team with this name already exists.", statusCode: StatusCodes.Status409Conflict);

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            ExternalTeamId = null,
            LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl
        };

        await _teams.AddAsync(team, cancellationToken);
        await _teams.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(team));
    }

    private static TeamResponse ToResponse(Team t) => new(t.Id, t.Name, t.ExternalTeamId, t.LogoUrl);
}
