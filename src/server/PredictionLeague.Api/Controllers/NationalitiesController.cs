using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PredictionLeague.Application.Abstractions.Persistence;

namespace PredictionLeague.Api.Controllers;

// Read-only lookup feeding client dropdowns. Any signed-in user can read.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NationalitiesController : ControllerBase
{
    private readonly INationalityRepository _nationalities;

    public NationalitiesController(INationalityRepository nationalities)
    {
        _nationalities = nationalities;
    }

    public record NationalityResponse(int Id, string Code, string Name);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var list = await _nationalities.ListAsync(cancellationToken);
        return Ok(list.Select(n => new NationalityResponse(n.Id, n.Code, n.Name)));
    }
}
