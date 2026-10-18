using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain;
using LabsApp.Domain.Dtos;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Controllers;

/// <summary>
/// Контроллер сдач (C-010, IF-012, FR-021): ведомость группы по семестру,
/// upsert дат сдачи и «свои сдачи» студента. Доступ — FR-022: grid/PUT —
/// только teacher, /me/submissions — только student; порядок отказов
/// фиксирован 401 → 403 → 400 → 404:
///  - 401 отдаёт схема аутентификации (Challenge с телом «Не авторизован»,
///    ADR-003); пользователь, удалённый из хранилища при живом access-токене,
///    даёт тот же 401 из действия (зеркало мока requireSessionUser);
///  - 403 — первая проверка внутри каждого действия с телом конверта
///    «Доступ запрещён»;
///  - semester — СТРОГИЙ контракт (ASM-018/ISS-011/AR-002, асимметрия с мягким
///    semester GET /labs): отсутствует/нечисловой/нецелый/вне 1..Labs__MaxSemester
///    → 400 errors.semester=['Семестр — число от 1 до N'] (шаблон рендерится
///    подстановкой ТЕКУЩЕЙ конфигурации) — и для grid, и для me;
///  - тело PUT разбирается вручную (без [FromBody]): синтаксически некорректный
///    JSON → 400 «Данные заполнены неверно» БЕЗ errors (IF-001); дата-поля
///    проверяются ДО разрешения сущностей (400 раньше 404): не null и не строгая
///    календарная 'YYYY-MM-DD' → errors{submitDate|defenseDate} со словарным
///    текстом date;
///  - 404: группа не найдена — «Группа не найдена»; PUT: студент не существует
///    или не студент — «Студент не найден», работа не существует — «Лабораторная
///    не найдена» (в порядке студент → работа);
///  - upsert по паре (studentId, labId) — атомарен в репозитории (IF-015): обе
///    даты заменяются переданными (null = сброс, запись пары сохраняется),
///    updatedBy = uuid преподавателя сессии, updatedAt = now (<see cref="TimeProvider"/>,
///    ADR-002); ответ 200 — ВСЕГДА полная запись SubmissionDto.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class SubmissionsController(
    IGroupRepository groups,
    IUserRepository users,
    ILabRepository labs,
    ISubmissionRepository submissions,
    TimeProvider timeProvider,
    IOptions<LabsOptions> labsOptions) : ControllerBase
{
    /// <summary>Размер страницы студентов ведомости (FR-021: по 5, ADR-109).</summary>
    public const int PageSize = 5;

    private const string SemesterField = "semester";
    private const string GroupIdField = "groupId";
    private const string StudentIdField = "studentId";
    private const string LabIdField = "labId";
    private const string SubmitDateField = "submitDate";
    private const string DefenseDateField = "defenseDate";

    private readonly IGroupRepository _groups = groups ?? throw new ArgumentNullException(nameof(groups));
    private readonly IUserRepository _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly ILabRepository _labs = labs ?? throw new ArgumentNullException(nameof(labs));
    private readonly ISubmissionRepository _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly int _maxSemester = labsOptions?.Value.MaxSemester
        ?? throw new ArgumentNullException(nameof(labsOptions));

    /// <summary>
    /// GET /api/v1/submissions?groupId=&amp;semester=&amp;page= — ведомость группы
    /// (FR-021): students — страница из <see cref="PageSize"/> студентов группы
    /// (fullName↑ затем login↑, русская локаль — <see cref="Collation.SortComparer"/>,
    /// AR-005); labs — работы семестра number↑; submissions — ТОЛЬКО пары
    /// студентов текущей страницы × работы семестра; total — полное число
    /// студентов группы; page — нормализованный номер (серверное расширение
    /// SubmissionsGridDto, AR-003: страница правее последней — пустые students
    /// при корректном total, эхо некорректного page запрещено). Пользователь
    /// сессии разрешается из хранилища (как в Update/Mine): удалённый из
    /// хранилища преподаватель при живом access-токене — 401 из действия.
    /// </summary>
    [HttpGet("submissions")]
    public IActionResult Grid(
        [FromQuery(Name = "groupId")] string? groupId,
        [FromQuery(Name = "semester")] string? semester,
        [FromQuery(Name = "page")] string? page)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        // Пользователь сессии существует в хранилище (зеркало мока
        // requireSessionUser): удалённая учётка при живом access-токене —
        // 401 из действия, ДО валидации query (тот же порядок, что в Update/Mine).
        if (TryLoadCurrentUser() is null)
        {
            return UnauthorizedEnvelope();
        }

        // Строгая валидация query (ISS-011/AR-002) — 400, не 200 с пустыми
        // массивами; нарушенные правила перечисляются ВСЕ сразу (semester +
        // groupId), как в полевой валидации тел.
        var (errors, semesterValue) = ValidateGridQuery(groupId, semester);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        if (_groups.GetById(ParseId(groupId)) is not { } group)
        {
            return GroupNotFound();
        }

        // Двухколоночная сортировка fullName↑ затем login↑ по правилам русской
        // локали без учёта регистра (AR-005) — сервисная обязанность контроллера
        // (репозиторий порядок не определяёт, IF-015). total — ДО нарезки страницы.
        var students = _users
            .ListByGroup(group.Id)
            .OrderBy(student => student.FullName, Collation.SortComparer)
            .ThenBy(student => student.Login, Collation.SortComparer)
            .ToList();

        // Смещение страницы — в long (CR-001): произведение (page-1)*PageSize,
        // посчитанное в int, переполняется (для pageSize=5 — уже с page ≥ 429 496 731)
        // и заворачивается в отрицательное — Skip отдал бы ПЕРВУЮ страницу вместо
        // пустой хвостовой. Ответ при этом эхом отдаёт нормализованный номер страницы.
        var normalizedPage = PageParam.Normalize(page);
        var skip = (long)(normalizedPage - 1) * PageSize;
        IEnumerable<User> slice = skip >= students.Count
            ? Enumerable.Empty<User>()
            : students.Skip((int)skip).Take(PageSize);
        var pageStudents = slice.ToList();

        var semesterLabs = _labs
            .ListByFilter(semesterValue)
            .OrderBy(lab => lab.Number)
            .ToList();

        // Только пары текущей страницы × работы семестра (FR-021): записи чужих
        // студентов и других семестров не отдаются ни при каких параметрах.
        var gridSubmissions = _submissions
            .GetByPairs(
                pageStudents.Select(student => student.Id).ToArray(),
                semesterLabs.Select(lab => lab.Id).ToArray())
            .Select(ToDto)
            .ToArray();

        return Ok(new SubmissionsGridDto
        {
            Students = pageStudents.Select(ToGridStudent).ToArray(),
            Labs = semesterLabs.Select(ToGridLab).ToArray(),
            Submissions = gridSubmissions,
            Total = students.Count,
            Page = normalizedPage,
        });
    }

    /// <summary>
    /// PUT /api/v1/submissions {studentId, labId, submitDate, defenseDate} —
    /// upsert даты сдачи (FR-021): дата-поля валидируются ДО разрешения
    /// сущностей (400 раньше 404); студент (существует и role=student) → иначе
    /// 404 «Студент не найден»; работа → иначе 404 «Лабораторная не найдена»;
    /// обе даты заменяются переданными (null = сброс, запись пары сохраняется);
    /// 200 — полная запись <see cref="SubmissionDto"/>.
    /// </summary>
    [HttpPut("submissions")]
    public async Task<IActionResult> Update(CancellationToken cancellationToken)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        if (TryLoadCurrentUser() is not { } teacher)
        {
            return UnauthorizedEnvelope();
        }

        using var document = await TryParseJsonBodyAsync(cancellationToken);
        if (document is null)
        {
            return BadRequestWithoutFieldErrors();
        }

        // Дата-поля ДО разрешения сущностей (IF-012 DATE_FORMAT): не null и не
        // строгая календарная 'YYYY-MM-DD' — errors{submitDate|defenseDate},
        // обе ошибки сразу. Только после этого резолвятся студент и работа.
        var (errors, submitDate, defenseDate) = ResolveDateFields(document.RootElement);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        // Не-uuid/нестроковые studentId/labId трактуются как несуществующие
        // сущности (404) — полевой валидации для них контракт не определяет.
        var studentId = ResolveIdField(document.RootElement, StudentIdField);
        var labId = ResolveIdField(document.RootElement, LabIdField);

        // Порядок 404: студент (существует И role=student) → работа.
        var student = _users.GetById(studentId);
        if (student is null || student.Role != UserRoles.Student)
        {
            return StudentNotFound();
        }

        if (_labs.GetById(labId) is null)
        {
            return LabNotFound();
        }

        // Атомарный upsert пары под StorageLock (IF-015): полная замена обеих
        // дат (null = сброс), метки — преподаватель сессии и TimeProvider.now.
        // Null-результат — нарушение контракта репозитория (IF-015: «всегда не
        // null»): fail-fast через InvalidOperationException → 500-конверт хоста
        // (IF-001), а не NullReferenceException вне конверта.
        var saved = _submissions.Upsert(
            studentId,
            labId,
            submitDate,
            defenseDate,
            teacher.Id,
            _timeProvider.GetUtcNow().UtcDateTime)
            ?? throw new InvalidOperationException(
                "Репозиторий сдач вернул null из Upsert (нарушение контракта IF-015).");

        return Ok(ToFullDto(saved));
    }

    /// <summary>
    /// GET /api/v1/me/submissions?semester= — сдачи текущего студента
    /// (FR-021, student-only; teacher → 403): semester строгий (те же правила,
    /// что у grid); студент без группы → 200 {hasGroup:false, labs:[],
    /// submissions:[]}; с группой → работы семестра number↑ и ТОЛЬКО свои
    /// сдачи (без studentId — он известен из сессии; чужие данные не отдаются
    /// ни при каких параметрах).
    /// </summary>
    [HttpGet("me/submissions")]
    public IActionResult Mine([FromQuery(Name = "semester")] string? semester)
    {
        if (DenyIfNotStudent() is { } denial)
        {
            return denial;
        }

        if (TryLoadCurrentUser() is not { } student)
        {
            return UnauthorizedEnvelope();
        }

        // Строгий semester (ISS-011/AR-002) обязателен и для студента без группы:
        // 400 раньше ветки hasGroup.
        if (!TryParseStrictSemester(semester, out var semesterValue))
        {
            return ValidationFailed(new Dictionary<string, string[]>
            {
                [SemesterField] = [ValidationTexts.LabSemesterText(_maxSemester)],
            });
        }

        if (student.GroupId is null)
        {
            // §4.4: вместо таблицы — предупреждение, данные работ не отдаются.
            return Ok(new MySubmissionsDto { HasGroup = false });
        }

        var semesterLabs = _labs
            .ListByFilter(semesterValue)
            .OrderBy(lab => lab.Number)
            .ToList();

        var mySubmissions = _submissions
            .ListForStudentAndSemester(
                student.Id,
                semesterLabs.Select(lab => lab.Id).ToArray())
            .Select(submission => new MySubmissionDto
            {
                LabId = submission.LabId.ToString(),
                SubmitDate = submission.SubmitDate,
                DefenseDate = submission.DefenseDate,
            })
            .ToArray();

        return Ok(new MySubmissionsDto
        {
            HasGroup = true,
            Labs = semesterLabs.Select(ToGridLab).ToArray(),
            Submissions = mySubmissions,
        });
    }

    // ------------------------------------------------------------------
    // Роль и конверт отказов
    // ------------------------------------------------------------------

    /// <summary>
    /// Проверка роли teacher (FR-022) — ПЕРВАЯ операция grid/PUT: 403 отдаётся
    /// раньше валидации query/тела (400) и поиска записей (404). null — доступ
    /// разрешён.
    /// </summary>
    private IActionResult? DenyIfNotTeacher() =>
        User.IsInRole(UserRoles.Teacher)
            ? null
            : ForbiddenEnvelope();

    /// <summary>
    /// Проверка роли student (FR-022, me/submissions): любая иная роль (в том
    /// числе teacher) → 403 «Доступ запрещён». null — доступ разрешён.
    /// </summary>
    private IActionResult? DenyIfNotStudent() =>
        User.IsInRole(UserRoles.Student)
            ? null
            : ForbiddenEnvelope();

    private static IActionResult ForbiddenEnvelope() =>
        new ObjectResult(new ErrorEnvelope { Message = ValidationTexts.Forbidden })
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/json" },
        };

    private static IActionResult UnauthorizedEnvelope() =>
        new ObjectResult(new ErrorEnvelope { Message = AuthCoreDefaults.UnauthorizedMessage })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/json" },
        };

    /// <summary>Текущий пользователь по claim sub (NameIdentifier); отсутствие
    /// claim при живой сессии либо удалённая запись → 401 из действия
    /// (зеркало мока requireSessionUser).</summary>
    private User? TryLoadCurrentUser()
    {
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawId, out var id) ? _users.GetById(id) : null;
    }

    private static IActionResult GroupNotFound() =>
        new NotFoundObjectResult(new ErrorEnvelope { Message = ValidationTexts.GroupNotFound })
        {
            ContentTypes = { "application/json" },
        };

    private static IActionResult StudentNotFound() =>
        new NotFoundObjectResult(new ErrorEnvelope { Message = ValidationTexts.StudentNotFound })
        {
            ContentTypes = { "application/json" },
        };

    private static IActionResult LabNotFound() =>
        new NotFoundObjectResult(new ErrorEnvelope { Message = ValidationTexts.LabNotFound })
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
    // Валидация query и тела
    // ------------------------------------------------------------------

    /// <summary>
    /// Строгая валидация query ведомости (IF-012 VALIDATION): semester —
    /// отсутствует/нечисловой/нецелый/вне 1..Labs__MaxSemester → текст-шаблон
    /// от ТЕКУЩЕЙ конфигурации; groupId — отсутствует/пустой → «Заполните поле».
    /// Обе ошибки возвращаются сразу.
    /// </summary>
    private (Dictionary<string, string[]> Errors, int Semester) ValidateGridQuery(
        string? groupId, string? semester)
    {
        var errors = new Dictionary<string, string[]>();

        if (!TryParseStrictSemester(semester, out var semesterValue))
        {
            errors[SemesterField] = [ValidationTexts.LabSemesterText(_maxSemester)];
        }

        if (string.IsNullOrWhiteSpace(groupId))
        {
            errors[GroupIdField] = [ValidationTexts.Required];
        }

        return (errors, semesterValue);
    }

    /// <summary>
    /// Строгий разбор semester (FR-021/ASM-018): целое 1..Labs__MaxSemester.
    /// Нечисловое, нецелое (дробное) и внедиапазонное значение — false
    /// (в отличие от мягкого semester GET /labs — ASM-018).
    /// </summary>
    private bool TryParseStrictSemester(string? raw, out int semester) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out semester)
        && semester >= 1
        && semester <= _maxSemester;

    /// <summary>
    /// Дата-поля submitDate/defenseDate (IF-012 DATE_FORMAT): литерал null —
    /// валидный сброс; строка — строгая календарная 'YYYY-MM-DD'
    /// (<see cref="FieldValidators.TryParseContractDate"/>); отсутствие поля,
    /// нестроковое значение и неконтрактная строка — ошибка словаря date по
    /// своему полю; обе ошибки накапливаются сразу.
    /// </summary>
    private static (Dictionary<string, string[]> Errors, DateOnly? SubmitDate, DateOnly? DefenseDate) ResolveDateFields(
        JsonElement root)
    {
        var errors = new Dictionary<string, string[]>();
        var submitDate = ResolveDateField(root, SubmitDateField, errors);
        var defenseDate = ResolveDateField(root, DefenseDateField, errors);
        return (errors, submitDate, defenseDate);
    }

    private static DateOnly? ResolveDateField(
        JsonElement root, string field, Dictionary<string, string[]> errors)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(field, out var element))
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    // null = сброс даты (контракт PUT: обе даты всегда целиком).
                    return null;
                case JsonValueKind.String:
                    if (FieldValidators.TryParseContractDate(element.GetString(), out var value))
                    {
                        return value;
                    }

                    break;
            }
        }

        errors[field] = [ValidationTexts.DateInvalid];
        return null;
    }

    /// <summary>
    /// uuid-поле тела (studentId/labId): строка, разбираемая как uuid, — её
    /// значение; отсутствие/нестрока/не-uuid — Guid.Empty (несуществующая
    /// сущность → 404, полевой валидации для идентификаторов контракт
    /// не определяет).
    /// </summary>
    private static Guid ResolveIdField(JsonElement root, string field) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(field, out var element)
        && element.ValueKind == JsonValueKind.String
        && Guid.TryParse(element.GetString(), out var id)
            ? id
            : Guid.Empty;

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

    /// <summary>uuid из query/маршрута; нечитаемая строка — Guid.Empty
    /// (несуществующая сущность → 404, зеркало ParseGroupId GroupsController).</summary>
    private static Guid ParseId(string? raw) =>
        Guid.TryParse(raw, out var id) ? id : Guid.Empty;

    private static GridStudentDto ToGridStudent(User student) => new()
    {
        Id = student.Id.ToString(),
        FullName = student.FullName,
    };

    private static GridLabDto ToGridLab(Lab lab) => new()
    {
        Id = lab.Id.ToString(),
        Number = lab.Number,
        DefenseRequired = lab.DefenseRequired,
    };

    private static GridSubmissionDto ToDto(Submission submission) => new()
    {
        StudentId = submission.StudentId.ToString(),
        LabId = submission.LabId.ToString(),
        SubmitDate = submission.SubmitDate,
        DefenseDate = submission.DefenseDate,
    };

    /// <summary>Полная запись сдачи в ответе PUT (Null-формы не существует):
    /// id/updatedAt/updatedBy ненулевые — метки проставлены upsert'ом.</summary>
    private static SubmissionDto ToFullDto(Submission submission) => new()
    {
        Id = submission.Id.ToString(),
        StudentId = submission.StudentId.ToString(),
        LabId = submission.LabId.ToString(),
        SubmitDate = submission.SubmitDate,
        DefenseDate = submission.DefenseDate,
        UpdatedAt = submission.UpdatedAt,
        UpdatedBy = submission.UpdatedBy?.ToString() ?? string.Empty,
    };
}
