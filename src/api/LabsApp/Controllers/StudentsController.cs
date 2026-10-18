using LabsApp.Domain;
using LabsApp.Domain.Dtos;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting;
using LabsApp.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Controllers;

/// <summary>
/// Контроллер студентов (C-009, IF-011, FR-020): постраничный перечень с
/// многословным одно-полевым поиском и фильтром группы, назначение/перевод/
/// снятие группы. Доступ — только teacher (FR-022); порядок отказов фиксирован
/// 401 → 403 → 400 → 404:
///  - 401 отдаёт схема аутентификации (Challenge с телом «Не авторизован», ADR-003);
///  - 403 — первая проверка внутри каждого действия (раньше валидации параметров,
///    разбора тела и поиска записей) с телом конверта «Доступ запрещён»;
///  - 400 — единственная полевая ветка перечня: search длиннее 200 символов после
///    трима → errors.search=['Поиск — не более 200 символов']; 400-ветки на
///    groupId НЕТ (IF-011);
///  - 404 — только PUT: id не существует или роль ≠ student → «Студент не найден»
///    (сущность маршрута проверяется раньше тела); не найдена группа по строке
///    groupId ЛИБО поле в состоянии Invalid → «Группа не найдена».
/// Три состояния поля groupId тела (ADR-014) НЕ схлопываются: JSON-строка —
/// включение/перевод, литерал null — снятие группы (204), отсутствующее/
/// нестроковое значение/битый JSON — 404 «Группа не найдена» без изменения
/// записи. Запись группы — УЗКАЯ атомарная мутация <see cref="IUserRepository.SetGroup"/>
/// (SEC-001): проверка студента и группы + смена ТОЛЬКО GroupId в одной
/// критической секции StorageLock — полный снимок записи не перезаписывается,
/// конкурентная смена пароля/профиля студента не откатывается. Фильтр groupId
/// перечня: отсутствует — без фильтра, «none» — только студенты без группы,
/// uuid — точное равенство; НЕИЗВЕСТНЫЙ uuid даёт пустую выборку, а НЕ 404.
/// Сортировка fullName↑ затем login↑ по правилам русской
/// локали (<see cref="Collation.SortComparer"/>, AR-005) применяется к ПОЛНОЙ
/// выборке до нарезки страницы; page нормализуется <see cref="PageParam"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/students")]
public sealed class StudentsController(IGroupRepository groups, IUserRepository users) : ControllerBase
{
    /// <summary>Размер страницы перечня студентов (FR-020: pageSize=10).</summary>
    public const int PageSize = 10;

    private const string SearchField = "search";
    private const string GroupIdField = "groupId";

    private readonly IGroupRepository _groups = groups ?? throw new ArgumentNullException(nameof(groups));
    private readonly IUserRepository _users = users ?? throw new ArgumentNullException(nameof(users));

    /// <summary>GET /api/v1/students — страница перечня (FR-020).</summary>
    [HttpGet]
    public IActionResult List(
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "groupId")] string? groupId,
        [FromQuery(Name = "page")] string? page)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        // Единственная полевая валидация (IF-011 SEARCH_TOO_LONG): длина ПОСЛЕ
        // трима; пустой/отсутствующий search валиден и фильтра не задаёт.
        if (FieldValidators.Search(search) is { Count: > 0 } searchErrors)
        {
            return ValidationFailed(new Dictionary<string, string[]> { [SearchField] = [.. searchErrors] });
        }

        // Фильтрация — репозиторий (IF-015): только role=student, многословный
        // одно-полевой ci-поиск и три-режимный фильтр группы («none»/uuid/нет).
        // Сортировка — обязанность сервиса ДО нарезки страницы (FR-020, AR-005).
        var students = _users
            .ListStudents(search, groupId)
            .OrderBy(student => student.FullName, Collation.SortComparer)
            .ThenBy(student => student.Login, Collation.SortComparer)
            .ToList();

        // Некорректная страница нормализуется к 1 без эха исходного значения;
        // страница правее последней — пустые items при корректном total.
        var normalizedPage = PageParam.Normalize(page);

        // Смещение страницы — в long (CR-001): для page ≥ 214 748 366 произведение
        // (page-1)*PageSize переполняет int и заворачивается в отрицательное,
        // после чего Skip отдал бы ПЕРВУЮ страницу вместо пустой хвостовой.
        // Ответ при этом эхом отдаёт нормализованный (исходный) номер страницы.
        var skip = (long)(normalizedPage - 1) * PageSize;
        IEnumerable<User> slice = skip >= students.Count
            ? Enumerable.Empty<User>()
            : students.Skip((int)skip).Take(PageSize);
        var items = slice
            .Select(ToDto)
            .ToArray();

        return Ok(new PagedResult<StudentDto>
        {
            Items = items,
            Total = students.Count,
            Page = normalizedPage,
            PageSize = PageSize,
        });
    }

    /// <summary>PUT /api/v1/students/{id}/group — назначение/перевод/снятие группы (FR-020).</summary>
    [HttpPut("{id}/group")]
    public async Task<IActionResult> SetGroup(string id, CancellationToken cancellationToken)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        // Сущность маршрута раньше тела (IF-001): id не существует ИЛИ роль ≠
        // student (включая uuid преподавателя) → единый 404 «Студент не найден».
        var userId = ParseUserId(id);
        if (_users.GetById(userId) is not { Role: UserRoles.Student })
        {
            return StudentNotFound();
        }

        // Lenient-чтение тела в ПОЛНОЙ трёхстатной проекции поля groupId (ADR-014):
        // состояния String/JsonNull/Invalid не схлопываются — 400-ветки нет.
        var body = await LenientBodyReader.FromStreamAsync(Request.Body, cancellationToken);

        switch (body.GetField(GroupIdField))
        {
            case { Kind: LenientFieldKind.String } field:
                // Любая строка, не равная id существующей группы (в т.ч. не-uuid),
                // — 404 «Группа не найдена», запись не меняется (IF-011). Проверка
                // группы и смена — одна критическая секция (SEC-001): делегат
                // проверяет группу под блокировкой мутации.
                return ToActionResult(_users.SetGroup(
                    userId,
                    ParseGroupId(field.Value),
                    GroupExists));

            case { Kind: LenientFieldKind.JsonNull }:
                // Литерал null — снятие группы: groupId=null, ответ 204 (FR-020).
                return ToActionResult(_users.SetGroup(userId, null, null));

            default:
                // Invalid: поле отсутствует, нестроковое либо тело не разобрано.
                return GroupNotFound();
        }
    }

    // ------------------------------------------------------------------
    // Роль и конверт отказов
    // ------------------------------------------------------------------

    /// <summary>
    /// Проверка роли teacher (FR-022) — ПЕРВАЯ операция действия: 403 отдаётся
    /// раньше валидации (400), разбора тела и поиска записи (404). null — доступ разрешён.
    /// </summary>
    private IActionResult? DenyIfNotTeacher() =>
        User.IsInRole(UserRoles.Teacher)
            ? null
            : new ObjectResult(new ErrorEnvelope { Message = ValidationTexts.Forbidden })
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/json" },
            };

    private static IActionResult StudentNotFound() =>
        new NotFoundObjectResult(new ErrorEnvelope { Message = ValidationTexts.StudentNotFound })
        {
            ContentTypes = { "application/json" },
        };

    private static IActionResult GroupNotFound() =>
        new NotFoundObjectResult(new ErrorEnvelope { Message = ValidationTexts.GroupNotFound })
        {
            ContentTypes = { "application/json" },
        };

    /// <summary>400 полевой валидации: конверт с errors {поле: тексты} (IF-001).</summary>
    private static IActionResult ValidationFailed(Dictionary<string, string[]> errors) =>
        new BadRequestObjectResult(new ErrorEnvelope { Message = ValidationTexts.InvalidData, Errors = errors })
        {
            ContentTypes = { "application/json" },
        };

    // ------------------------------------------------------------------
    // Вспомогательное
    // ------------------------------------------------------------------

    /// <summary>uuid из маршрута; не-uuid трактуется как несуществующий студент (404).</summary>
    private static Guid ParseUserId(string raw) =>
        Guid.TryParse(raw, out var id) ? id : Guid.Empty;

    /// <summary>uuid из поля groupId тела; не-uuid трактуется как несуществующая группа (404).</summary>
    private static Guid ParseGroupId(string? raw) =>
        Guid.TryParse(raw, out var id) ? id : Guid.Empty;

    /// <summary>
    /// Проверка существования группы для узкой мутации <see cref="IUserRepository.SetGroup"/>
    /// (SEC-001): вызывается внутри критической секции — единый замок хранилища
    /// реентерабелен, поэтому чтение группы из-под мутации атомарно с ней.
    /// </summary>
    private bool GroupExists(Guid groupId) => _groups.GetById(groupId) is not null;

    /// <summary>Итог узкой мутации группы → статусы ответа (IF-011).</summary>
    private IActionResult ToActionResult(SetGroupResult result) => result switch
    {
        SetGroupResult.Success => NoContent(),
        SetGroupResult.StudentNotFound => StudentNotFound(),
        _ => GroupNotFound(),
    };

    /// <summary>
    /// StudentDto строки перечня (IF-011): groupId/groupName — текущая группа
    /// студента либо null (каскад удаления группы не оставляет висячих ссылок).
    /// </summary>
    private StudentDto ToDto(User student)
    {
        var group = student.GroupId is { } groupId ? _groups.GetById(groupId) : null;
        return new StudentDto
        {
            Id = student.Id.ToString(),
            FullName = student.FullName,
            Login = student.Login,
            Email = student.Email,
            GroupId = student.GroupId?.ToString(),
            GroupName = group?.Name,
        };
    }
}
