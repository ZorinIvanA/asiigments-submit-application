using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Controllers;

/// <summary>
/// Контроллер смены пароля текущего пользователя (C-011, IF-013, FR-016):
/// PUT /api/v1/me/password {currentPassword, password, confirmPassword} → 204.
/// Отдельный файл/контроллер от ProfileController (T-012 — параллельность с
/// T-011, один файл — один писатель).
/// Доступ — любая аутентифицированная роль (FR-022 role=user); анонимно — 401
/// «Не авторизован» от схемы аутентификации (Challenge, ADR-003); пользователь,
/// удалённый из хранилища при живом access-токене, даёт тот же 401 (зеркало
/// мока requireSessionUser).
///
/// Фиксированный порядок (FR-016):
///  1. lenient-чтение тела (ADR-014): отсутствующие/нестроковые/null-поля —
///     пустые строки; синтаксически некорректный/пустой JSON → 400 «Данные
///     заполнены неверно» БЕЗ errors и БЕЗ KDF (зеркало login, ADR-014/IF-001);
///  2. currentPassword проверяется ПЕРВЫМ, до полевых ошибок нового пароля,
///     дословно без трима (пробел — легальный спецзнак в пределах 128): длина
///     &gt;128 → 400 WRONG_CURRENT_PASSWORD «Неверный текущий пароль» без
///     errors-карты БЕЗ выполнения KDF (Δkdf=0); иначе ровно одна деривация
///     Verify (метка change_password) → несовпадение → тот же 400 без errors
///     (Δkdf=1) — появление errors.password означало бы обратный порядок;
///  3. валидация password/confirmPassword по FR-006 (8–128, тексты словаря
///     включая password.max; пароль — сырая строка без трима, нарушенные
///     правила — все сразу; повтор обязателен и дословно равен паролю) →
///     400 «Данные заполнены неверно» + errors {password, confirmPassword};
///  4. успех: passwordHash перезаписывается УЗКОЙ атомарной мутацией
///     IUserRepository.SetPassword — только PasswordHash, конкурентные правка
///     профиля и группа студента не откатываются (CR-001/SEC-001); ровно одна
///     деривация Hash (метка change_password), false мутатора (запись удалена
///     в полёте) → 401 «Не авторизован» без отзыва и событий; отзываются ВСЕ
///     refresh-токены пользователя КРОМЕ совпадающего с refresh-cookie текущего
///     запроса — cookie нет, отзываются все (арбитраж ISS-002, обязательная
///     семантика IF-013), ответ 204.
/// Δkdf-гейт (FR-027): неверный currentPassword → 1; сверхдлинный (&gt;128) → 0;
/// успех → 2 (verify + hash).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/me/password")]
public sealed class MePasswordController(
    IUserRepository users,
    IPasswordHasher hasher,
    ISecurityTokenRepository securityTokens,
    ISecurityEventLogger securityEvents) : ControllerBase
{
    private const string CurrentPasswordField = "currentPassword";
    private const string PasswordField = "password";
    private const string ConfirmPasswordField = "confirmPassword";

    private readonly IUserRepository _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly IPasswordHasher _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
    private readonly ISecurityTokenRepository _securityTokens =
        securityTokens ?? throw new ArgumentNullException(nameof(securityTokens));
    private readonly ISecurityEventLogger _securityEvents =
        securityEvents ?? throw new ArgumentNullException(nameof(securityEvents));

    /// <summary>PUT /api/v1/me/password — смена пароля (FR-016): 204; 400/401.</summary>
    [HttpPut]
    public async Task<IActionResult> Change(CancellationToken cancellationToken)
    {
        // (0) Текущий пользователь: без claim/записи при живой сессии — 401
        // «Не авторизован» (зеркало мока requireSessionUser, FR-022: 401 раньше 400).
        if (TryLoadCurrentUser() is not { } user)
        {
            return UnauthorizedEnvelope();
        }

        // (1) Lenient-чтение: битый/пустой JSON → 400 «Данные заполнены неверно»
        // БЕЗ errors и БЕЗ KDF (зеркало login, ADR-014).
        var body = await LenientBodyReader.FromStreamAsync(Request.Body, cancellationToken);
        if (!body.ParseSuccess)
        {
            return BadRequestWithoutFieldErrors();
        }

        // (2) currentPassword — ПЕРВЫМ и дословно: гейт длины >128 ДО любой
        // деривации (Δkdf=0, тот же 400 WRONG_CURRENT_PASSWORD — FR-016/ISS-016),
        // затем ровно одна деривация Verify (Δkdf=1 при несовпадении).
        var currentPassword = body.GetStringOrNull(CurrentPasswordField) ?? string.Empty;
        if (currentPassword.Length > FieldValidators.PasswordMaxLength
            || !_hasher.Verify(currentPassword, user.PasswordHash, KdfCallers.ChangePassword))
        {
            return WrongCurrentPassword();
        }

        // (3) Полевая валидация нового пароля — ПОСЛЕ проверки текущего (FR-016):
        // все ошибки обоих полей сразу (зеркало формы «Смена пароля»).
        var password = body.GetStringOrNull(PasswordField) ?? string.Empty;
        var confirmPassword = body.GetStringOrNull(ConfirmPasswordField) ?? string.Empty;

        var errors = new Dictionary<string, string[]>();
        Collect(errors, PasswordField, FieldValidators.Password(password));
        Collect(errors, ConfirmPasswordField, ConfirmPasswordErrors(password, confirmPassword));
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        // (4) Успех: узкая атомарная мутация пароля (CR-001/SEC-001) —
        // IUserRepository.SetPassword меняет ТОЛЬКО PasswordHash хранимой записи:
        // login/роль/группа/профиль не перечитываются и не откатываются устаревшим
        // снимком; конкурентные правка профиля и SetGroup сохраняются.
        if (!_users.SetPassword(user.Id, _hasher.Hash(password, KdfCallers.ChangePassword)))
        {
            // Пользователь удалён из хранилища между TryLoadCurrentUser и мутацией
            // (IF-015 NOT_FOUND → 401, IF-013): без отзыва токенов и без событий —
            // пароль фактически не менялся, отзыв «вслепую» отозвал бы чужую сессию.
            return UnauthorizedEnvelope();
        }

        // Арбитраж ISS-002: отзываются ВСЕ refresh-токены пользователя, КРОМЕ
        // совпадающего с refresh-cookie текущего запроса (в хранилище лежит
        // SHA-256-хэш значения — IF-003); cookie нет — отзываются все.
        _securityTokens.RevokeAllForUserExcept(user.Id, ExceptTokenHashFromCookie());

        // Факты безопасности (IF-016/ADR-034) — после фактического применения
        // нового пароля и отзыва, до ответа 204; без значений токенов (NFR-006).
        _securityEvents.LogPasswordChanged();
        _securityEvents.LogRefreshTokenRevoked(SecurityEventReasons.PasswordChange);

        return NoContent();
    }

    // ------------------------------------------------------------------
    // Текущий пользователь, refresh-cookie и конверт отказов
    // ------------------------------------------------------------------

    /// <summary>
    /// Текущий пользователь по claim sub (NameIdentifier). Отсутствие claim при
    /// живой сессии либо запись, удалённая из хранилища, дают null — действие
    /// отвечает 401 «Не авторизован» (зеркало мока requireSessionUser).
    /// </summary>
    private User? TryLoadCurrentUser()
    {
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawId, out var id) ? _users.GetById(id) : null;
    }

    /// <summary>
    /// Хэш значения refresh-cookie текущего запроса либо null, когда cookie нет:
    /// он передаётся репозиторию как exceptTokenHash (арбитраж ISS-002).
    /// </summary>
    private string? ExceptTokenHashFromCookie()
    {
        var value = Request.Cookies[AuthCoreDefaults.RefreshTokenCookieName];
        return string.IsNullOrEmpty(value) ? null : Sha256Hex(value);
    }

    /// <summary>400 WRONG_CURRENT_PASSWORD: одно сообщение, БЕЗ errors-карты (IF-013).</summary>
    private static IActionResult WrongCurrentPassword() =>
        new BadRequestObjectResult(new ErrorEnvelope { Message = ValidationTexts.WrongCurrentPassword })
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

    private static IActionResult UnauthorizedEnvelope() =>
        new ObjectResult(new ErrorEnvelope { Message = AuthCoreDefaults.UnauthorizedMessage })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/json" },
        };

    /// <summary>Накопление полевых ошибок: пустой перечень поле не создаёт.</summary>
    private static void Collect(Dictionary<string, string[]> errors, string field, IReadOnlyList<string> texts)
    {
        if (texts.Count > 0)
        {
            errors[field] = [.. texts];
        }
    }

    /// <summary>
    /// Правила confirmPassword (FR-006, зеркало repeatPassword регистрации):
    /// поле обязательно (пустое и пробельное — required); дальше — дословное
    /// равенство без трима (mismatch не выдаётся, если password пуст — его
    /// ошибки уже в errors.password).
    /// </summary>
    private static IReadOnlyList<string> ConfirmPasswordErrors(string password, string confirm)
    {
        if (string.IsNullOrWhiteSpace(confirm))
        {
            return [ValidationTexts.Required];
        }

        return FieldValidators.PasswordMatch(password, confirm);
    }

    /// <summary>SHA-256 hex (строчные) значения refresh-токена — зеркало TokenService (IF-003).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
