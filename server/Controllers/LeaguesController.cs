using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Models;

namespace PredictionLeague.Controllers;

// Skeleton endpoints for the league lifecycle (FR-006/007/008).
// In-memory placeholder store — swap for a real data layer when persistence lands.
[ApiController]
[Route("api/[controller]")]
public class LeaguesController : ControllerBase
{
    private static readonly List<League> Store = [];

    [HttpGet]
    public IEnumerable<League> GetAll() => Store;

    [HttpGet("{id:guid}")]
    public ActionResult<League> GetById(Guid id)
    {
        var league = Store.FirstOrDefault(l => l.Id == id);
        return league is null ? NotFound() : league;
    }

    [HttpPost]
    public ActionResult<League> Create(CreateLeagueRequest request)
    {
        var league = new League
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            TournamentId = request.TournamentId,
            OrganizerUserId = request.OrganizerUserId,
            InviteCode = Guid.NewGuid().ToString("N")[..8]
        };
        Store.Add(league);
        return CreatedAtAction(nameof(GetById), new { id = league.Id }, league);
    }
}

public record CreateLeagueRequest(string Name, Guid TournamentId, Guid OrganizerUserId);
