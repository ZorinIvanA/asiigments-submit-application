using System.Text;
using System.Text.Json;
using LabsApp.Domain.Dtos;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Controllers;

/// <summary>
/// Контроллер профиля текущего пользователя (C-011, IF-013, FR-015):
/// GET /api/v1/me/profile → 200 ProfileDto {login, email, fullName, role,
/// groupName}; PUT /api/v1/me/profile {fullName, email} → валидация FR-006
/// (обе ошибки сразу) → 400; дубликат lower(email) среди ДРУГИХ пользователей
/// → 409 «Пользователь с таким email уже существует»; успех — обновление
/// ТОЛЬКО fullName/email (login, роль и группа неизменны, поле login во входе
/// игнорируется), 200 обновлённый ProfileDto. Текущий пользователь —
/// засеянный преподаватель (Seed__TeacherLogin): без аутентификации
/// приложение работает от его имени.
/// groupName вычисляется по ТЕКУЩЕМУ состоянию групп (зеркало мока
/// toProfileDto: переименование/удаление группы студента сразу видно в профиле).
/// Порядок отказов фиксирован 401 → 400 → 409:
///  - 401 — сид-преподаватель не найден в хранилище (сид не выполнен);
///  - тело запроса разбирается вручную (без [FromBody]): синтаксически
///    некорректный JSON → 400 «Данные заполнены неверно» БЕЗ errors (IF-001);
///  - полевая валидация возвращает ВСЕ ошибки сразу (errors {fullName, email}),
///    отсутствующее/null/нестроковое поле — пустая строка (зеркало мока asString);
///  - 409 проверяется после валидации по ci-индексу репозитория (совпадение с
///    самим собой — не конфликт); авторитетная ci-проверка и запись — УЗКАЯ
///    атомарная мутация <see cref="IUserRepository.UpdateProfileAsync"/> (CR-001/
///    SEC-001, IF-015): конкурентные смена пароля и группы не откатываются,
///    200 рендерится по свежему снимку.
/// </summary>
[ApiController]
[Route("api/v1/me/profile")]
public sealed class ProfileController(
    IUserRepository users,
    IGroupRepository groups,
    IOptions<SeedOptions> seedOptions) : ControllerBase
{
    private const string FullNameField = "fullName";
    private const string EmailField = "email";

    private readonly IUserRepository _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly IGroupRepository _groups = groups ?? throw new ArgumentNullException(nameof(groups));
    private readonly SeedOptions _seedOptions = seedOptions?.Value
        ?? throw new ArgumentNullException(nameof(seedOptions));

    /// <summary>GET /api/v1/me/profile — ProfileDto текущего пользователя (FR-015).</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return await TryLoadCurrentUserAsync() is { } user ? Ok(await ToDtoAsync(user)) : UnauthorizedEnvelope();
    }

    /// <summary>PUT /api/v1/me/profile — правка fullName/email (FR-015): 400 → 409 → 200.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(CancellationToken cancellationToken)
    {
        if (await TryLoadCurrentUserAsync() is not { } user)
        {
            return UnauthorizedEnvelope();
        }

        // Порядок 400 → 409: полевая валидация раньше проверки занятости email.
        using var document = await TryParseJsonBodyAsync(cancellationToken);
        if (document is null)
        {
            return BadRequestWithoutFieldErrors();
        }

        var (errors, fullName, email) = ValidateAndNormalize(document.RootElement);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        // Дубликат lower(email) среди ДРУГИХ пользователей (IF-013 CONFLICT_EMAIL);
        // ci-поиск репозитория = Collation.Key, совпадение с самим собой — не конфликт.
        if (await _users.GetByEmailAsync(email) is { } owner && owner.Id != user.Id)
        {
            return DuplicateEmail();
        }

        // Обновляются ТОЛЬКО fullName/email УЗКОЙ атомарной мутацией (CR-001/
        // SEC-001): login, роль, группа, хэш пароля и CreatedAt мутатором не
        // перечитываются и не перезаписываются — конкурентные смена пароля
        // (SetPassword) и назначение группы (SetGroup) не откатываются (IF-013).
        // Авторитетная ci-проверка занятости email ДРУГИМ пользователем и
        // перепривязка byEmail — внутри мутатора атомарно
        // (IF-015 EMAIL_CONFLICT: гонка двух PUT на один email → ровно один 200).
        // Поле login во входе игнорируется (FR-015).
        return await _users.UpdateProfileAsync(user.Id, fullName, email) switch
        {
            UpdateProfileResult.EmailConflict => DuplicateEmail(),

            // Защитная ветка удалённого пользователя (IF-015 NOT_FOUND → 401,
            // зеркало TryLoadCurrentUser): запись исчезла между чтением и мутацией.
            UpdateProfileResult.UserNotFound => UnauthorizedEnvelope(),

            _ => await TryLoadCurrentUserAsync() is { } fresh
                ? Ok(await ToDtoAsync(fresh))

                // 200 рендерится по СВЕЖЕМУ снимку (groupName актуален); null
                // сразу после Success — запись удалена в полёте → 401-конверт.
                : UnauthorizedEnvelope(),
        };
    }

    // ------------------------------------------------------------------
    // Текущий пользователь и конверт отказов
    // ------------------------------------------------------------------

    /// <summary>
    /// Текущий пользователь — засеянный преподаватель (Seed__TeacherLogin):
    /// без аутентификации приложение работает от его имени. Отсутствие записи
    /// (сид не выполнен) даёт null — действие отвечает 401 «Не авторизован».
    /// </summary>
    private async Task<User?> TryLoadCurrentUserAsync()
    {
        var login = _seedOptions.TeacherLogin?.Trim();
        var normalized = string.IsNullOrEmpty(login) ? SeedOptions.DefaultTeacherLogin : login;
        return await _users.GetByLoginAsync(normalized);
    }

    private static IActionResult UnauthorizedEnvelope() =>
        new ObjectResult(new ErrorEnvelope { Message = ValidationTexts.Unauthorized })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/json" },
        };

    private static IActionResult DuplicateEmail() =>
        new ConflictObjectResult(new ErrorEnvelope { Message = ValidationTexts.DuplicateEmail })
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
    // Разбор и валидация тела ProfileInput
    // ------------------------------------------------------------------

    /// <summary>
    /// Полевая валидация FR-006 (ВСЕ ошибки сразу): правила и тексты —
    /// <see cref="FieldValidators.FullName"/>/<see cref="FieldValidators.Email"/>;
    /// в БД идут триммированные значения (зеркало мока profile.update).
    /// </summary>
    private static (Dictionary<string, string[]> Errors, string FullName, string Email) ValidateAndNormalize(
        JsonElement root)
    {
        var errors = new Dictionary<string, string[]>();

        var fullName = AsStringField(root, FullNameField);
        if (FieldValidators.FullName(fullName) is { Count: > 0 } fullNameErrors)
        {
            errors[FullNameField] = [.. fullNameErrors];
        }

        var email = AsStringField(root, EmailField);
        if (FieldValidators.Email(email) is { Count: > 0 } emailErrors)
        {
            errors[EmailField] = [.. emailErrors];
        }

        return (errors, fullName.Trim(), email.Trim());
    }

    /// <summary>
    /// Строковое значение поля либо пустая строка: отсутствие, литерал null и
    /// нестроковый тип означают «значения нет» (зеркало мока asString → required).
    /// </summary>
    private static string AsStringField(JsonElement root, string field) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(field, out var element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : string.Empty;

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

    /// <summary>ProfileDto: groupName — по ТЕКУЩЕМУ состоянию групп (IF-013).</summary>
    private async Task<ProfileDto> ToDtoAsync(User user) => new()
    {
        Login = user.Login,
        Email = user.Email,
        FullName = user.FullName,
        Role = user.Role,
        GroupName = user.GroupId is { } groupId ? (await _groups.GetByIdAsync(groupId))?.Name : null,
    };
}
