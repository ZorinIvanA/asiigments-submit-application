using LabsApp.Domain.Entities;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LabsApp.Controllers;

/// <summary>
/// Контроллер перечня семестров (C-007, IF-009, FR-018): GET /api/v1/semesters →
/// 200 int[] — различные номера семестров, по которым есть хотя бы одна работа,
/// по возрастанию.
/// </summary>
[ApiController]
[Route("api/v1/semesters")]
public sealed class SemestersController(ILabRepository labs) : ControllerBase
{
    private readonly ILabRepository _labs = labs ?? throw new ArgumentNullException(nameof(labs));

    /// <summary>GET /api/v1/semesters — distinct семестры по возрастанию (FR-018).</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var semesters = (await _labs.GetAllAsync())
            .Select(lab => lab.Semester)
            .Distinct()
            .OrderBy(semester => semester)
            .ToArray();

        return Ok(semesters);
    }
}
