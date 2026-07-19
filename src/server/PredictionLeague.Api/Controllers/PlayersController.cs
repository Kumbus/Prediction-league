using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions;
using PredictionLeague.Application.Abstractions.Persistence;
using PredictionLeague.Application.Abstractions.Players;
using PredictionLeague.Domain.Entities;
using PredictionLeague.Infrastructure.Identity;

namespace PredictionLeague.Api.Controllers;

// Admin maintenance for Players + CSV bulk import (FR-005). No delete: MatchEvent.PlayerId is
// a non-null FK and orphan handling is out of scope this slice.
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class PlayersController : ControllerBase
{
    private const int MaxPageSize = 100;
    private const long CsvSizeCapBytes = 2 * 1024 * 1024;

    private readonly IPlayerRepository _players;
    private readonly INationalityRepository _nationalities;
    private readonly ITournamentRepository _tournaments;
    private readonly IPlayerCsvImporter _importer;

    public PlayersController(
        IPlayerRepository players,
        INationalityRepository nationalities,
        ITournamentRepository tournaments,
        IPlayerCsvImporter importer)
    {
        _players = players;
        _nationalities = nationalities;
        _tournaments = tournaments;
        _importer = importer;
    }

    public record PlayerResponse(
        Guid Id,
        string Name,
        int ExternalPlayerId,
        int? NationalityId,
        PlayerPosition Position,
        DateOnly? DateOfBirth,
        int? HeightCm,
        Guid? ClubTeamId,
        Guid? NationalTeamId);

    public record PagedPlayersResponse(IReadOnlyList<PlayerResponse> Items, int Total, int Page, int PageSize);

    public record CreatePlayerRequest(
        string Name,
        int? ExternalPlayerId,
        int? NationalityId,
        PlayerPosition? Position,
        DateOnly? DateOfBirth,
        int? HeightCm,
        Guid? ClubTeamId,
        Guid? NationalTeamId);

    public record PatchPlayerRequest(
        string? Name,
        int? ExternalPlayerId,
        int? NationalityId,
        PlayerPosition? Position,
        DateOnly? DateOfBirth,
        int? HeightCm,
        Guid? ClubTeamId,
        Guid? NationalTeamId);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var result = await _players.ListAsync(page, pageSize, cancellationToken);
        return Ok(new PagedPlayersResponse(
            result.Items.Select(ToResponse).ToList(),
            result.Total,
            result.Page,
            result.PageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var player = await _players.GetByIdAsync(id, cancellationToken);
        return player is null ? NotFound() : Ok(ToResponse(player));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePlayerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(detail: "Name is required.", statusCode: StatusCodes.Status400BadRequest);

        if (request.NationalityId.HasValue)
        {
            var nat = (await _nationalities.ListAsync(cancellationToken))
                .FirstOrDefault(n => n.Id == request.NationalityId.Value);
            if (nat is null)
                return Problem(detail: "NationalityId not found.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ExternalPlayerId.HasValue)
        {
            var collision = await _players.GetByExternalPlayerIdAsync(request.ExternalPlayerId.Value, cancellationToken);
            if (collision is not null)
                return Problem(detail: "ExternalPlayerId is already in use.", statusCode: StatusCodes.Status409Conflict);
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ExternalPlayerId = request.ExternalPlayerId ?? 0,
            NationalityId = request.NationalityId,
            Position = request.Position ?? PlayerPosition.Unknown,
            DateOfBirth = request.DateOfBirth,
            HeightCm = request.HeightCm,
            ClubTeamId = request.ClubTeamId,
            NationalTeamId = request.NationalTeamId
        };

        await _players.AddAsync(player, cancellationToken);
        await _players.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = player.Id }, ToResponse(player));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Patch(Guid id, PatchPlayerRequest request, CancellationToken cancellationToken)
    {
        var player = await _players.GetByIdAsync(id, cancellationToken);
        if (player is null) return NotFound();

        if (request.ExternalPlayerId.HasValue)
        {
            var collision = await _players.GetByExternalPlayerIdAsync(request.ExternalPlayerId.Value, cancellationToken);
            if (collision is not null && collision.Id != player.Id)
                return Problem(detail: "ExternalPlayerId is already in use.", statusCode: StatusCodes.Status409Conflict);
        }

        if (!string.IsNullOrWhiteSpace(request.Name)) player.Name = request.Name;
        if (request.ExternalPlayerId.HasValue) player.ExternalPlayerId = request.ExternalPlayerId.Value;
        if (request.NationalityId.HasValue) player.NationalityId = request.NationalityId;
        if (request.Position.HasValue) player.Position = request.Position.Value;
        if (request.DateOfBirth.HasValue) player.DateOfBirth = request.DateOfBirth;
        if (request.HeightCm.HasValue) player.HeightCm = request.HeightCm;
        if (request.ClubTeamId.HasValue) player.ClubTeamId = request.ClubTeamId;
        if (request.NationalTeamId.HasValue) player.NationalTeamId = request.NationalTeamId;

        _players.Update(player);
        await _players.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(player));
    }

    [HttpPost("import")]
    [RequestSizeLimit(CsvSizeCapBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CsvSizeCapBytes)]
    public async Task<IActionResult> Import(
        IFormFile file,
        [FromQuery] bool dryRun = true,
        [FromQuery] Guid? tournamentId = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return Problem(detail: "CSV file is required.", statusCode: StatusCodes.Status400BadRequest);

        if (tournamentId.HasValue)
        {
            var tournament = await _tournaments.GetByIdAsync(tournamentId.Value, cancellationToken);
            if (tournament is null) return NotFound();
        }

        await using var stream = file.OpenReadStream();
        try
        {
            if (dryRun)
            {
                var preview = await _importer.PreviewAsync(stream, tournamentId, cancellationToken);
                return Ok(preview);
            }

            var result = await _importer.CommitAsync(stream, tournamentId, cancellationToken);
            return Ok(result);
        }
        catch (CsvImportException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static PlayerResponse ToResponse(Player p)
        => new(p.Id, p.Name, p.ExternalPlayerId, p.NationalityId, p.Position,
            p.DateOfBirth, p.HeightCm, p.ClubTeamId, p.NationalTeamId);
}
