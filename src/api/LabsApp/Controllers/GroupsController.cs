using System.Text;
using System.Text.Json;
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
/// Контроллер групп (C-008, IF-010, FR-019): перечень без пагинации с вычисляемым
/// studentCount и сортировкой name↑ без учёта регистра ПО ПРАВИЛАМ РУССКОЙ ЛОКАЛИ
/// (<see cref="Collation.SortComparer"/>, AR-005), создание/переименование/удаление
/// и постраничный состав группы (fullName↑ затем login↑, pageSize=10). Доступ —
/// только teacher (FR-022); порядок отказов фиксирован 401 → 403 → 400 → 404 → 409:
///  - 401 отдаёт схема аутентификации (Challenge с телом «Не авторизован», ADR-003);
///  - 403 — первая проверка внутри каждого действия (раньше разбора тела, валидации
///    и поиска записи) с телом конверта «Доступ запрещён»;
///  - тело запроса разбирается вручную (без [FromBody]): синтаксически некорректный
///    JSON → 400 «Данные заполнены неверно» БЕЗ errors (IF-001);
///  - поле name: отсутствие/null/нестроковое значение, пустая или пробельная строка
///    после трима и длина свыше 100 дают ЕДИНСТВЕННЫЙ полевой текст «Название
///    группы — от 1 до 100 символов» (IF-010 VALIDATION, зеркало мока
///    groups.create: validateGroupName вне 1–100 — один текст group.name);
///  - 409 — дубликат lower(name) по ci-индексу репозитория (Collation.Key,
///    ADR-013), при update — кроме самой группы; атомарность проверки+записи под
///    общим StorageLock — <see cref="StorageConflictException"/> (IF-015);
///  - DELETE: 404 при неизвестном id, иначе 204 — каскад репозитория сбрасывает
///    GroupId студентов группы (учётные записи сохраняются, студенты становятся
///    «без группы», IF-015).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/groups")]
public sealed class GroupsController(IGroupRepository groups, IUserRepository users, TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Размер страницы состава группы (FR-019: pageSize=10).</summary>
    public const int PageSize = 10;

    private const string NameField = "name";

    private readonly IGroupRepository _groups = groups ?? throw new ArgumentNullException(nameof(groups));
    private readonly IUserRepository _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>GET /api/v1/groups — весь перечень GroupDto[], name↑ ru (FR-019).</summary>
    [HttpGet]
    public IActionResult List()
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        // Сортировка по русской локали без учёта регистра (AR-005) — сервисная
        // обязанность контроллера: репозиторий порядок не определяёт (IF-015).
        var items = _groups
            .List()
            .OrderBy(group => group.Name, Collation.SortComparer)
            .Select(ToDto)
            .ToArray();

        return Ok(items);
    }

    /// <summary>POST /api/v1/groups — создание: 400 → 409 → 201 (FR-019).</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        using var document = await TryParseJsonBodyAsync(cancellationToken);
        if (document is null)
        {
            return BadRequestWithoutFieldErrors();
        }

        var (errors, name) = ValidateAndNormalize(document.RootElement);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };

        try
        {
            // Атомарная ci-проверка имени + вставка (IF-015): дубликат → 409.
            _groups.Add(group);
        }
        catch (StorageConflictException)
        {
            return DuplicateGroup();
        }

        // studentCount вычисляется по текущему состоянию (не хранится, data_design);
        // у новой группы студентов нет — CountByGroup возвращает 0 (IF-010).
        return StatusCode(StatusCodes.Status201Created, ToDto(group));
    }

    /// <summary>PUT /api/v1/groups/{id} — переименование: 400 → 404 → 409 → 200 (FR-019).</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, CancellationToken cancellationToken)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        // Порядок 400 → 404 → 409 (IF-010): валидация тела раньше поиска записи,
        // поиск раньше проверки занятости имени.
        using var document = await TryParseJsonBodyAsync(cancellationToken);
        if (document is null)
        {
            return BadRequestWithoutFieldErrors();
        }

        var (errors, name) = ValidateAndNormalize(document.RootElement);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        if (_groups.GetById(ParseGroupId(id)) is not { } existing)
        {
            return GroupNotFound();
        }

        // Дубликат lower(name) КРОМЕ самой группы (IF-010 CONFLICT_GROUP):
        // ci-поиск репозитория = Collation.Key, совпадение с самим собой — не конфликт.
        if (_groups.GetByName(name) is { } nameOwner && nameOwner.Id != existing.Id)
        {
            return DuplicateGroup();
        }

        var updated = new Group
        {
            // id группы сохраняется; CreatedAt не меняется.
            Id = existing.Id,
            Name = name,
            CreatedAt = existing.CreatedAt,
        };

        try
        {
            // Атомарная замена с перепривязкой ci-индекса под StorageLock (IF-015):
            // гонка двух PUT на одно имя → 409 по исключению репозитория.
            _groups.Update(updated);
        }
        catch (StorageConflictException)
        {
            return DuplicateGroup();
        }

        return Ok(ToDto(updated));
    }

    /// <summary>DELETE /api/v1/groups/{id} — 204; студентам группы groupId=null (FR-019).</summary>
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        if (_groups.GetById(ParseGroupId(id)) is null)
        {
            return GroupNotFound();
        }

        // Каскад «запись группы → сброс GroupId её студентов» в критической секции
        // репозитория (IF-015): учётные записи сохраняются со «свободным» статусом.
        _groups.Delete(ParseGroupId(id));
        return NoContent();
    }

    /// <summary>GET /api/v1/groups/{id}/students?page= — страница состава (FR-019).</summary>
    [HttpGet("{id}/students")]
    public IActionResult Students(string id, [FromQuery(Name = "page")] string? page)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        if (_groups.GetById(ParseGroupId(id)) is not { } group)
        {
            return GroupNotFound();
        }

        // Двухколоночная сортировка fullName↑ затем login↑ по правилам русской
        // локали без учёта регистра (AR-005, Collation.SortComparer).
        var students = _users
            .ListByGroup(group.Id)
            .OrderBy(student => student.FullName, Collation.SortComparer)
            .ThenBy(student => student.Login, Collation.SortComparer)
            .ToList();

        // Некорректная страница нормализуется к 1 без эха исходного значения;
        // страница правее последней — пустые items при корректном total (IF-009).
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
            .Select(student => ToDto(student, group))
            .ToArray();

        return Ok(new PagedResult<StudentDto>
        {
            Items = items,
            Total = students.Count,
            Page = normalizedPage,
            PageSize = PageSize,
        });
    }

    // ------------------------------------------------------------------
    // Роль и конверт отказов
    // ------------------------------------------------------------------

    /// <summary>
    /// Проверка роли teacher (FR-022) — ПЕРВАЯ операция действия: 403 отдаётся
    /// раньше валидации тела (400) и поиска записи (404). null — доступ разрешён.
    /// </summary>
    private IActionResult? DenyIfNotTeacher() =>
        User.IsInRole(UserRoles.Teacher)
            ? null
            : new ObjectResult(new ErrorEnvelope { Message = ValidationTexts.Forbidden })
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/json" },
            };

    private static IActionResult GroupNotFound() =>
        new NotFoundObjectResult(new ErrorEnvelope { Message = ValidationTexts.GroupNotFound })
        {
            ContentTypes = { "application/json" },
        };

    private static IActionResult DuplicateGroup() =>
        new ConflictObjectResult(new ErrorEnvelope { Message = ValidationTexts.DuplicateGroup })
        {
            ContentTypes = { "application/json" },
        };

    /// <summary>400 полевой валидации: конверт с errors {поле: тексты} (IF-001).</summary>
    private static IActionResult ValidationFailed(Dictionary<string, string[]> errors) =>
        new BadRequestObjectResult(new ErrorEnvelope { Message = ValidationTexts.InvalidData, Errors = errors })
        {
            ContentTypes = { "application/json" },
        };

    /// <summary>400 синтаксически некорректного JSON тела — БЕЗ errors (IF-001).</summary>
    private static IActionResult BadRequestWithoutFieldErrors() =>
        new BadRequestObjectResult(new ErrorEnvelope { Message = ValidationTexts.InvalidData })
        {
            ContentTypes = { "application/json" },
        };

    // ------------------------------------------------------------------
    // Разбор и валидация тела GroupNameInput
    // ------------------------------------------------------------------

    /// <summary>
    /// Поле name (IF-010 VALIDATION): вне 1–100 символов после трима — ЕДИНЫЙ текст
    /// group.name «Название группы — от 1 до 100 символов» (пустая/пробельная строка
    /// невалидна тем же текстом, зеркало мока groups.create); валидное значение
    /// возвращается триммированным. Отсутствующее, null и нестроковое поле —
    /// пустая строка (зеркало мока asString).
    /// </summary>
    private static (Dictionary<string, string[]> Errors, string Name) ValidateAndNormalize(
        JsonElement root)
    {
        var name = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(NameField, out var element)
            && element.ValueKind == JsonValueKind.String
                ? element.GetString()!
                : string.Empty;

        var trimmed = name.Trim();
        var errors = new Dictionary<string, string[]>();
        if (trimmed.Length == 0 || trimmed.Length > FieldValidators.GroupNameMaxLength)
        {
            errors[NameField] = [ValidationTexts.GroupNameLength];
        }

        return (errors, trimmed);
    }

    /// <summary>
    /// Читает тело запроса как UTF-8 и разбирает JSON. Битый/пустой JSON → null
    /// (400 без errors); исключение парсинга наружу не покидает.
    /// </summary>
    private async Task<JsonDocument?> TryParseJsonBodyAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Вспомогательное
    // ------------------------------------------------------------------

    /// <summary>uuid из маршрута; не-uuid трактуется как несуществующая группа (404).</summary>
    private static Guid ParseGroupId(string raw) =>
        Guid.TryParse(raw, out var id) ? id : Guid.Empty;

    /// <summary>GroupDto: studentCount вычисляется по текущему состоянию (data_design).</summary>
    private GroupDto ToDto(Group group) => new()
    {
        Id = group.Id.ToString(),
        Name = group.Name,
        StudentCount = _users.CountByGroup(group.Id),
    };

    /// <summary>
    /// StudentDto строки состава группы (IF-010): groupId — id группы, groupName —
    /// её имя (в составе группы оба константны, аменда 6 — зеркало мока).
    /// </summary>
    private static StudentDto ToDto(User student, Group group) => new()
    {
        Id = student.Id.ToString(),
        FullName = student.FullName,
        Login = student.Login,
        Email = student.Email,
        GroupId = group.Id.ToString(),
        GroupName = group.Name,
    };
}
