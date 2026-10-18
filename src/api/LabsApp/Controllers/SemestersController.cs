using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabsApp.Controllers;

/// <summary>
/// Контроллер перечня семестров (C-007, IF-009, FR-018): GET /api/v1/semesters →
/// 200 int[] — различные номера семестров, по которым есть хотя бы одна работа,
/// по возрастанию. Доступ — любая аутентифицированная роль (student/teacher);
/// анонимно — 401 «Не авторизован» от схемы аутентификации (Challenge, ADR-003);
/// роль дополнительно не проверяется (FR-022: role=user).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/semesters")]
public sealed class SemestersController(ILabRepository labs) : ControllerBase
{
    private readonly ILabRepository _labs = labs ?? throw new ArgumentNullException(nameof(labs));

    /// <summary>GET /api/v1/semesters — distinct семестры по возрастанию (FR-018).</summary>
    [HttpGet]
    public IActionResult List()
    {
        var semesters = _labs
            .GetAll()
            .Select(lab => lab.Semester)
            .Distinct()
            .OrderBy(semester => semester)
            .ToArray();

        return Ok(semesters);
    }
}
