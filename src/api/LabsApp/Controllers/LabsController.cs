using System.Globalization;
using System.Text;
using System.Text.Json;
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
/// Контроллер лабораторных работ (C-007, IF-009, FR-017): список с мягким
/// фильтром semester (ASM-018), двухколоночной сортировкой и пагинацией,
/// чтение одной записи, создание/правка/удаление с каскадом сдач.
/// Доступ — только teacher (FR-022); порядок отказов фиксирован
/// 401 → 403 → 400 → 404 → 409:
///  - 401 отдаёт схема аутентификации (Challenge с телом «Не авторизован», ADR-003);
///  - 403 — первая проверка внутри каждого действия (раньше разбора тела,
///    валидации и поиска записи) с телом конверта «Доступ запрещён»;
///  - тело запроса разбирается вручную (без [FromBody]): синтаксически
///    некорректный JSON → 400 «Данные заполнены неверно» без errors (IF-001),
///    полевая валидация возвращает ВСЕ ошибки сразу (errors {number, semester,
///    content, assignmentUrl, defenseRequired}); поэтому фреймворковых 400 на
///    этих действиях не возникает вовсе;
///  - нормализация полей — после успешной валидации (числа → int, трим,
///    пустая ссылка → null, defenseRequired — строго boolean, зеркало мока);
///  - 409 отдаётся по <see cref="StorageConflictException"/> репозитория:
///    проверка пары (semester, number) и запись атомарны под общим StorageLock
///    (IF-015); при update собственная запись конфликтом не считается.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/labs")]
public sealed class LabsController(ILabRepository labs, TimeProvider timeProvider, IOptions<LabsOptions> labsOptions) : ControllerBase
{
    /// <summary>Размер страницы списка работ (FR-017: pageSize=10).</summary>
    public const int PageSize = 10;

    private const string NumberField = "number";
    private const string SemesterField = "semester";
    private const string ContentField = "content";
    private const string AssignmentUrlField = "assignmentUrl";
    private const string DefenseRequiredField = "defenseRequired";

    /// <summary>
    /// Текст отказа не-boolean значения defenseRequired (TS-114): словарём v2.2
    /// текст для поля не определён (тест утверждает только статус и отсутствие
    /// создания), поэтому в <see cref="ValidationTexts"/> он НЕ вводится —
    /// словарь остаётся ровно перечню глоссария.
    /// </summary>
    private const string DefenseRequiredNotBoolean = "Поле defenseRequired должно быть true или false";

    private readonly ILabRepository _labs = labs ?? throw new ArgumentNullException(nameof(labs));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly int _maxSemester = labsOptions?.Value.MaxSemester
        ?? throw new ArgumentNullException(nameof(labsOptions));

    /// <summary>GET /api/v1/labs — страница списка (FR-017).</summary>
    [HttpGet]
    public IActionResult List(
        [FromQuery(Name = "semester")] string? semester,
        [FromQuery(Name = "page")] string? page,
        [FromQuery(Name = "sortField")] string? sortField,
        [FromQuery(Name = "sortDir")] string? sortDir)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        // Мягкая нормализация semester (FR-017/ASM-018): параметр отсутствует или
        // не парсится в целое (нечисловое, дробное) → фильтр НЕ применяется;
        // целое (в том числе вне 1..MaxSemester) → фильтр точного равенства,
        // внедиапазонное значение даёт пустую выборку, а не 400.
        var semesterFilter = int.TryParse(semester, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSemester)
            ? (int?)parsedSemester
            : null;

        // Двухколоночная сортировка (FR-017): первичный ключ sortField × sortDir
        // (значения вне словаря → дефолты semester/asc), вторичный — соседнее
        // поле всегда asc; дефолт совпадает с контрактом semester↑,number↑.
        var byNumber = string.Equals(sortField, "number", StringComparison.Ordinal);
        var descending = string.Equals(sortDir, "desc", StringComparison.Ordinal);

        var filtered = semesterFilter is null
            ? _labs.GetAll()
            : _labs.GetAll().Where(lab => lab.Semester == semesterFilter.Value).ToList();

        IOrderedEnumerable<Lab> ordered = (byNumber, descending) switch
        {
            (false, false) => filtered.OrderBy(lab => lab.Semester).ThenBy(lab => lab.Number),
            (false, true) => filtered.OrderByDescending(lab => lab.Semester).ThenBy(lab => lab.Number),
            (true, false) => filtered.OrderBy(lab => lab.Number).ThenBy(lab => lab.Semester),
            (true, true) => filtered.OrderByDescending(lab => lab.Number).ThenBy(lab => lab.Semester),
        };

        // Некорректная страница нормализуется к 1 без эха исходного значения;
        // страница правее последней — пустые items при корректном total.
        var normalizedPage = PageParam.Normalize(page);

        // Смещение страницы — в long (CR-001): для page ≥ 214 748 366 произведение
        // (page-1)*PageSize переполняет int и заворачивается в отрицательное,
        // после чего Skip отдал бы ПЕРВУЮ страницу вместо пустой хвостовой.
        // Ответ при этом эхом отдаёт нормализованный (исходный) номер страницы.
        var skip = (long)(normalizedPage - 1) * PageSize;
        IEnumerable<Lab> slice = skip >= filtered.Count
            ? Enumerable.Empty<Lab>()
            : ordered.Skip((int)skip).Take(PageSize);
        var items = slice
            .Select(ToDto)
            .ToArray();

        return Ok(new PagedResult<LabDto>
        {
            Items = items,
            Total = filtered.Count,
            Page = normalizedPage,
            PageSize = PageSize,
        });
    }

    /// <summary>GET /api/v1/labs/{id} — одна работа либо 404 NOT_FOUND_LAB.</summary>
    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        return _labs.GetById(ParseLabId(id)) is { } lab ? Ok(ToDto(lab)) : LabNotFound();
    }

    /// <summary>POST /api/v1/labs — создание: 400 → 409 → 201 (FR-017).</summary>
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

        var (errors, draft) = ValidateAndNormalize(document.RootElement, requireDefenseRequired: true);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = draft.Semester,
            Number = draft.Number,
            Content = draft.Content,
            AssignmentUrl = draft.AssignmentUrl,
            DefenseRequired = draft.DefenseRequired,
            CreatedAt = now,
            UpdatedAt = now,
        };

        try
        {
            // Атомарная проверка пары + вставка (IF-015): дубликат → 409.
            _labs.Add(lab);
        }
        catch (StorageConflictException)
        {
            return DuplicateLab();
        }

        return StatusCode(StatusCodes.Status201Created, ToDto(lab));
    }

    /// <summary>PUT /api/v1/labs/{id} — правка: 400 → 404 → 409 → 200 (FR-017).</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, CancellationToken cancellationToken)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        // Порядок 400 → 404 → 409: валидация тела раньше поиска записи,
        // поиск раньше проверки уникальности пары.
        using var document = await TryParseJsonBodyAsync(cancellationToken);
        if (document is null)
        {
            return BadRequestWithoutFieldErrors();
        }

        var (errors, draft) = ValidateAndNormalize(document.RootElement, requireDefenseRequired: false);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        if (_labs.GetById(ParseLabId(id)) is not { } existing)
        {
            return LabNotFound();
        }

        var updated = new Lab
        {
            // id записи сохраняется (IF-009); CreatedAt не меняется.
            Id = existing.Id,
            Semester = draft.Semester,
            Number = draft.Number,
            Content = draft.Content,
            AssignmentUrl = draft.AssignmentUrl,
            DefenseRequired = draft.DefenseRequired,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt,
        };

        try
        {
            // Репозиторий проставляет UpdatedAt; конфликт пары с ДРУГОЙ записью → 409
            // (собственная пара конфликтом не считается — StorageLock-атомарно, IF-015).
            _labs.Update(updated);
        }
        catch (StorageConflictException)
        {
            return DuplicateLab();
        }

        return Ok(ToDto(updated));
    }

    /// <summary>DELETE /api/v1/labs/{id} — 204 + каскад сдач (FR-017).</summary>
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (DenyIfNotTeacher() is { } denial)
        {
            return denial;
        }

        var labId = ParseLabId(id);
        if (_labs.GetById(labId) is null)
        {
            return LabNotFound();
        }

        // Каскадное удаление сдач работы — в критической секции репозитория (IF-015).
        _labs.Delete(labId);
        return NoContent();
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

    private static IActionResult LabNotFound() =>
        new NotFoundObjectResult(new ErrorEnvelope { Message = ValidationTexts.LabNotFound })
        {
            ContentTypes = { "application/json" },
        };

    private static IActionResult DuplicateLab() =>
        new ConflictObjectResult(new ErrorEnvelope { Message = ValidationTexts.DuplicateLab })
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
    // Разбор и валидация тела LabInput
    // ------------------------------------------------------------------

    private sealed record LabDraft(int Semester, int Number, string Content, string? AssignmentUrl, bool DefenseRequired);

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

    /// <summary>
    /// Полевая валидация LabInput (FR-017): ВСЕ ошибки сразу; при их отсутствии —
    /// нормализованный черновик (числа → int, трим, ''→null, строго boolean).
    /// Тексты — из словаря <see cref="ValidationTexts"/>; форматные правила —
    /// <see cref="FieldValidators"/> (зеркало клиентских валидаторов). ЕДИНСТВЕННОЕ
    /// исключение из словарного инварианта: не-boolean значение defenseRequired
    /// даёт контроллерную константу <see cref="DefenseRequiredNotBoolean"/> —
    /// в словаре v2.2 текст для этого поля не определён и сознательно не вводится
    /// (см. комментарий константы).
    /// <paramref name="requireDefenseRequired"/>: POST требует явного boolean
    /// defenseRequired (строгий контракт TS-114 — отсутствие поля отклоняется),
    /// PUT допускает отсутствие — нормализация в false (контракт TS-097:
    /// PUT-тело без поля валидно, «PUT наследует полевую валидацию LabInput»).
    /// </summary>
    private (Dictionary<string, string[]> Errors, LabDraft Draft) ValidateAndNormalize(
        JsonElement root, bool requireDefenseRequired)
    {
        var errors = new Dictionary<string, string[]>();

        var (numberErrors, number) = ResolveIntegerField(root, NumberField, requirePositive: true);
        if (numberErrors is not null)
        {
            errors[NumberField] = [.. numberErrors];
        }
        else if (FieldValidators.LabNumber(number) is { Count: > 0 } numberRange)
        {
            errors[NumberField] = [.. numberRange];
        }

        var (semesterErrors, semester) = ResolveIntegerField(root, SemesterField, requirePositive: false);
        if (semesterErrors is not null)
        {
            errors[SemesterField] = [.. semesterErrors];
        }
        else if (FieldValidators.Semester(semester, _maxSemester) is { Count: > 0 } semesterRange)
        {
            errors[SemesterField] = [.. semesterRange];
        }

        var (contentErrors, content) = ResolveContentField(root);
        if (contentErrors is not null)
        {
            errors[ContentField] = [.. contentErrors];
        }

        var (urlErrors, assignmentUrl) = ResolveAssignmentUrlField(root);
        if (urlErrors is not null)
        {
            errors[AssignmentUrlField] = [.. urlErrors];
        }

        var (defenseErrors, defenseRequired) = ResolveDefenseRequiredField(root, requireDefenseRequired);
        if (defenseErrors is not null)
        {
            errors[DefenseRequiredField] = [.. defenseErrors];
        }

        var draft = new LabDraft(
            Semester: semester,
            Number: number,
            Content: content,
            AssignmentUrl: assignmentUrl,
            DefenseRequired: defenseRequired);

        return (errors, draft);
    }

    /// <summary>
    /// Целое поле (number/semester): отсутствует/null/пустая строка после трима →
    /// «Заполните поле»; JSON-число (только целые токены) или строка из цифр
    /// (<see cref="FieldValidators.TryParseInteger"/>) → значение; иной мусор →
    /// текст формата поля (форматные тексты различаются полем).
    /// </summary>
    private (IReadOnlyList<string>? Errors, int Value) ResolveIntegerField(
        JsonElement root, string field, bool requirePositive)
    {
        var element = GetPresentField(root, field);
        if (element is null)
        {
            return ([ValidationTexts.Required], 0);
        }

        int? parsed;
        if (element.Value.ValueKind == JsonValueKind.String)
        {
            var raw = element.Value.GetString()!.Trim();
            if (raw.Length == 0)
            {
                return ([ValidationTexts.Required], 0);
            }

            parsed = FieldValidators.TryParseInteger(raw, out var value) ? value : null;
        }
        else if (element.Value.ValueKind == JsonValueKind.Number)
        {
            // Дробные и переполняющие int токены целыми не считаются.
            parsed = element.Value.TryGetInt32(out var value) ? value : null;
        }
        else
        {
            parsed = null;
        }

        if (parsed is null)
        {
            return (requirePositive ? [ValidationTexts.LabNumber] : [ValidationTexts.LabSemesterText(_maxSemester)], 0);
        }

        return (null, parsed.Value);
    }

    /// <summary>content: строка 1–500 после трима; отсутствует/null/нестрока → required.</summary>
    private static (IReadOnlyList<string>? Errors, string Value) ResolveContentField(JsonElement root)
    {
        var element = GetPresentField(root, ContentField);
        if (element is null || element.Value.ValueKind != JsonValueKind.String)
        {
            return ([ValidationTexts.Required], string.Empty);
        }

        var raw = element.Value.GetString()!;
        var errors = FieldValidators.LabContent(raw);
        return (errors.Count > 0 ? errors : null, raw.Trim());
    }

    /// <summary>
    /// assignmentUrl: null/пусто после трима → null (валидно); строка — префикс
    /// http(s) и ≤1000 (<see cref="FieldValidators.AssignmentUrl"/>); нестроковое
    /// значение нормализуется в null (зеркало мока: непустая строка не образуется).
    /// </summary>
    private static (IReadOnlyList<string>? Errors, string? Value) ResolveAssignmentUrlField(JsonElement root)
    {
        var element = GetPresentField(root, AssignmentUrlField);
        if (element is null)
        {
            return (null, null);
        }

        if (element.Value.ValueKind != JsonValueKind.String)
        {
            return (null, null);
        }

        var raw = element.Value.GetString()!;
        var errors = FieldValidators.AssignmentUrl(raw);
        if (errors.Count > 0)
        {
            return (errors, null);
        }

        var trimmed = raw.Trim();
        return (null, trimmed.Length == 0 ? null : trimmed);
    }

    /// <summary>
    /// defenseRequired — СТРОГО boolean (FR-017, TS-114): валидны только литералы
    /// true/false; строка ('yes'/'true'), число (1), объект, массив и литерал null —
    /// отказ полевой валидации. Отсутствие поля: POST — ошибка required (создание
    /// требует явного решения), PUT — допустимо (нормализация в false, TS-097).
    /// Нигде нестрогое приведение к bool не выполняется.
    /// </summary>
    private static (IReadOnlyList<string>? Errors, bool Value) ResolveDefenseRequiredField(
        JsonElement root, bool requirePresent)
    {
        var element = GetPresentField(root, DefenseRequiredField);
        if (element is null)
        {
            return requirePresent ? ([ValidationTexts.Required], false) : (null, false);
        }

        if (element.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return ([DefenseRequiredNotBoolean], false);
        }

        return (null, element.Value.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// Поле объекта при наличии и не-null значении; отсутствие поля и литерал null
    /// неразличимы (оба означают «значения нет» — зеркало isEmptyInput клиента).
    /// </summary>
    private static JsonElement? GetPresentField(JsonElement root, string field) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(field, out var element)
        && element.ValueKind != JsonValueKind.Null
            ? element
            : null;

    // ------------------------------------------------------------------
    // Вспомогательное
    // ------------------------------------------------------------------

    /// <summary>uuid из маршрута; не-uuid трактуется как несуществующая запись (404).</summary>
    private static Guid ParseLabId(string raw) =>
        Guid.TryParse(raw, out var id) ? id : Guid.Empty;

    private static LabDto ToDto(Lab lab) => new()
    {
        Id = lab.Id.ToString(),
        Semester = lab.Semester,
        Number = lab.Number,
        Content = lab.Content,
        AssignmentUrl = lab.AssignmentUrl,
        DefenseRequired = lab.DefenseRequired,
    };
}
