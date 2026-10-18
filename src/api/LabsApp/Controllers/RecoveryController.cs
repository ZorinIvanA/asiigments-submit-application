using System.Security.Cryptography;
using System.Text;
using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Controllers;

/// <summary>
/// Контроллер восстановления пароля (C-006, IF-008): recovery/request,
/// recovery/confirm, reset-password. Доступ — анонимный на всех трёх эндпойнтах
/// (FR-022); аутентификация не требуется и не выполняется.
///
/// POST /auth/recovery/request (FR-012) — фиксированный порядок:
///  1. TryAcquire лимитера recovery_request (3/3600с на lower(trim(email)),
///     IF-006) — ДО проверки существования email: 429 «Слишком много попыток.
///     Повторите позже» одинаков для существующего и несуществующего адреса
///     (оракула нет, FR-004);
///  2. код восстановления (6 ASCII-цифр CSPRNG, ведущие нули допустимы)
///     генерируется ВСЕГДА — и для существующего, и для несуществующего email;
///  3. существующий email: все прежние живые коды гасятся атомарно и
///     записывается новый RecoveryCode (солёный SHA-256 кода — НЕ KDF, ASM-005;
///     TTL 10 минут; attempts=0) — одновременно жив максимум один код (resend);
///  4. IEmailSender.SendAsync вызывается для ОБЕИХ веток с одинаковым форматом
///     записи (IF-005); сбой доставки ответ не меняет;
///  5. ответ 200 с ПУСТЫМ телом (0 байт, Content-Length: 0, НЕ '{}' — ISS-014,
///     ADR-012) в любом случае. Операций KDF в эндпойнте — 0 (FR-004/ASM-005).
///
/// POST /auth/recovery/confirm (FR-013): email и code триммятся; пользователь не
/// найден / живого кода нет / хэш не совпал / код просрочен или погашен — единый
/// 400 «Код восстановления не подходит» без раскрытия существования; при
/// несовпадении attempts инкрементируется у живого кода, 5-я неверная попытка
/// гасит код (хранилище, domain_model live → annulled); совпадение: код гасится
/// (usedAt=now), выпускается PasswordResetToken (непрозрачная строка ≥256 бит,
/// в хранилище только SHA-256, TTL 15 минут — ASM-002/IF-003), ответ
/// 200 {resetToken}. KDF — 0.
///
/// POST /auth/reset-password (FR-014): resetToken триммится, поиск по SHA-256;
/// не найден / просрочен / использован → 400 «Ссылка восстановления
/// недействительна или истекла» (найденный неживой токен дополнительно гасится);
/// живой токен → полевая валидация password/confirmPassword по FR-006 (токен
/// при полевых ошибках НЕ гасится, Δkdf=0); успех: перезапись passwordHash
/// УЗКОЙ атомарной мутацией IUserRepository.SetPassword — только PasswordHash,
/// конкурентные правка профиля и группа студента не откатываются (CR-001/
/// SEC-001); ровно одна деривация (метка reset_password), гашение ВСЕХ
/// reset-токенов пользователя, отзыв ВСЕХ его refresh-токенов, Warning-факты безопасности
/// IF-016 (LogPasswordReset + LogRefreshTokenRevoked('password_reset')) и ответ
/// 204 с пустым телом. Защитная ветка недоступной записи (SetPassword=false,
/// IF-015 NOT_FOUND) — 401 «Не авторизован» без гашения токена, отзыва и событий.
/// Δkdf-гейт (FR-027): успех — 1, все ветки отказа токена/полей — 0.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class RecoveryController(
    IUserRepository users,
    ITokenService tokens,
    IPasswordHasher hasher,
    IEmailSender emailSender,
    ISecurityTokenRepository securityTokens,
    RecoveryRequestLimiter recoveryRequests,
    ISecurityEventLogger securityEvents,
    TimeProvider timeProvider,
    ILogger<RecoveryController> logger) : ControllerBase
{
    private const string EmailField = "email";
    private const string CodeField = "code";
    private const string ResetTokenField = "resetToken";
    private const string PasswordField = "password";
    private const string ConfirmPasswordField = "confirmPassword";

    /// <summary>TTL кода восстановления (FR-012: expiresAt = создание + 10 минут).</summary>
    private const int RecoveryCodeTtlMinutes = 10;

    /// <summary>Тема письма с кодом (IF-005: текст уходит в «EmailDev», NFR-006).</summary>
    private const string RecoveryEmailSubject = "Код восстановления пароля";

    private readonly IUserRepository _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly ITokenService _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private readonly IPasswordHasher _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
    private readonly IEmailSender _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
    private readonly ISecurityTokenRepository _securityTokens =
        securityTokens ?? throw new ArgumentNullException(nameof(securityTokens));
    private readonly RecoveryRequestLimiter _recoveryRequests =
        recoveryRequests ?? throw new ArgumentNullException(nameof(recoveryRequests));
    private readonly ISecurityEventLogger _securityEvents =
        securityEvents ?? throw new ArgumentNullException(nameof(securityEvents));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<RecoveryController> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    // ------------------------------------------------------------------
    // POST /auth/recovery/request (FR-012)
    // ------------------------------------------------------------------

    /// <summary>POST /api/v1/auth/recovery/request — выдача кода (всегда 200, пустое тело; 429).</summary>
    [HttpPost("recovery/request")]
    public async Task<IActionResult> RequestCode(CancellationToken cancellationToken)
    {
        // (0) Lenient-чтение тела: любое значение email, включая отсутствующее,
        // нестроковое и null → пустая строка (ADR-014); лимитеру нужен только ключ.
        var body = await LenientBodyReader.FromStreamAsync(Request.Body, cancellationToken);
        var email = body.GetStringOrNull(EmailField) ?? string.Empty;

        // (1) Лимитер ДО проверки существования (IF-006/FR-004): 429 одинаков для
        // существующего и несуществующего email — оракула нет.
        if (!_recoveryRequests.TryAcquire(email))
        {
            return RateLimited();
        }

        var trimmedEmail = email.Trim();

        // (2) Код генерируется ВСЕГДА (FR-012); хэш — быстрый солёный SHA-256 (не KDF).
        var code = _tokens.GenerateRecoveryCode();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // (3) Существующий email: прежние живые коды гасятся атомарно в той же
        // критической секции, что и вставка нового (IF-015, resend).
        if (_users.GetByEmail(trimmedEmail) is { } user)
        {
            _securityTokens.AddLive(new RecoveryCode
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CodeHash = _tokens.HashRecoveryCode(code),
                ExpiresAt = now.AddMinutes(RecoveryCodeTtlMinutes),
                UsedAt = null,
                Attempts = 0,
                CreatedAt = now,
            });
        }

        // (4) Письмо — для ОБЕИХ веток, формат записи одинаковый (IF-005); код
        // появляется в журнале только в категории «EmailDev» (NFR-006). Сбой
        // доставки ответ НЕ меняет (IF-005: «ошибки отправки не меняют HTTP-ответ
        // recovery/request»): исключение фиксируется записью Error без адресата
        // и содержимого письма (адресат и код — не для журнала, NFR-006), ответ —
        // прежний 200 с пустым телом. Отмена запроса — не сбой доставки: наружу.
        try
        {
            await _emailSender.SendAsync(
                trimmedEmail,
                RecoveryEmailSubject,
                $"Ваш код восстановления: {code}. Код действует 10 минут.");
        }
        catch (Exception deliveryFailure) when (deliveryFailure is not OperationCanceledException)
        {
            _logger.LogError(
                deliveryFailure,
                "Доставка письма восстановления не выполнена: канал IEmailSender недоступен "
                + "(recovery/request); HTTP-ответ запроса не меняется (IF-005).");
        }

        // (5) 200 с ПУСТЫМ телом — 0 байт, НЕ '{}' (ISS-014/ADR-012).
        return Ok();
    }

    // ------------------------------------------------------------------
    // POST /auth/recovery/confirm (FR-013)
    // ------------------------------------------------------------------

    /// <summary>POST /api/v1/auth/recovery/confirm — подтверждение кода (200 {resetToken}; 400).</summary>
    [HttpPost("recovery/confirm")]
    public async Task<IActionResult> Confirm(CancellationToken cancellationToken)
    {
        // (1) Разбор JSON: битое тело → 400 «Данные заполнены неверно» без errors
        // (IF-001, зеркало login/me-password).
        var body = await LenientBodyReader.FromStreamAsync(Request.Body, cancellationToken);
        if (!body.ParseSuccess)
        {
            return BadRequestWithoutFieldErrors();
        }

        // (2) email и code триммятся (IF-008).
        var email = (body.GetStringOrNull(EmailField) ?? string.Empty).Trim();
        var code = (body.GetStringOrNull(CodeField) ?? string.Empty).Trim();

        // (3) Пользователь не найден → единый 400 без раскрытия существования.
        if (_users.GetByEmail(email) is not { } user)
        {
            return CodeRejected();
        }

        // (4) Живой код (usedAt=null и expiresAt>now) — максимум один (инвариант AddLive).
        if (_securityTokens.FindLiveForUser(user.Id) is not { } live)
        {
            return CodeRejected();
        }

        // (5) Несовпадение: attempts++ у живого кода (5-я неверная гасит код в
        // хранилище, IF-015) — тот же единый 400.
        if (!_tokens.VerifyRecoveryCode(code, live.CodeHash))
        {
            _securityTokens.IncrementAttemptsOnLive(live.Id);
            return CodeRejected();
        }

        // (6) Успех: код гасится, выпускается reset-токен (непрозрачный, в
        // хранилище только SHA-256, TTL 15 минут — ASM-002).
        _securityTokens.MarkUsed(live.Id);
        var grant = _tokens.CreatePasswordResetToken(user.Id);
        _securityTokens.Add(new PasswordResetToken
        {
            TokenHash = grant.TokenHash,
            UserId = user.Id,
            ExpiresAt = grant.ExpiresAt,
            UsedAt = null,
        });

        return Ok(new { resetToken = grant.Value });
    }

    // ------------------------------------------------------------------
    // POST /auth/reset-password (FR-014)
    // ------------------------------------------------------------------

    /// <summary>POST /api/v1/auth/reset-password — сброс пароля по токену (204; 400).</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(CancellationToken cancellationToken)
    {
        // (1) Разбор JSON: битое тело → 400 «Данные заполнены неверно» без errors.
        var body = await LenientBodyReader.FromStreamAsync(Request.Body, cancellationToken);
        if (!body.ParseSuccess)
        {
            return BadRequestWithoutFieldErrors();
        }

        // (2) resetToken триммится (IF-008); пароль — сырая строка без трима (FR-006).
        var resetToken = (body.GetStringOrNull(ResetTokenField) ?? string.Empty).Trim();
        var password = body.GetStringOrNull(PasswordField) ?? string.Empty;
        var confirmPassword = body.GetStringOrNull(ConfirmPasswordField) ?? string.Empty;

        // (3) Поиск ЖИВОГО токена по SHA-256 предъявленного значения (IF-015):
        // не найден / просрочен / использован → единый 400; найденный неживой
        // дополнительно гасится (MarkUsed для отсутствующего дайджеста —
        // отсутствие операции, FR-014). Пользователь, удалённый из хранилища,
        // делает токен неприменимым — та же ветка отказа.
        var tokenHash = Sha256Hex(resetToken);
        if (_securityTokens.FindLiveResetByHash(tokenHash) is not { } record
            || _users.GetById(record.UserId) is not { } user)
        {
            _securityTokens.MarkUsed(tokenHash);
            return ResetLinkInvalid();
        }

        // (4) Полевая валидация password/confirmPassword по FR-006 — ПОСЛЕ
        // проверки токена; токен НЕ гасится, Δkdf=0.
        var errors = new Dictionary<string, string[]>();
        Collect(errors, PasswordField, FieldValidators.Password(password));
        Collect(errors, ConfirmPasswordField, ConfirmPasswordErrors(password, confirmPassword));
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        // (5) Успех: ровно одна деривация Hash (метка reset_password), УЗКАЯ
        // атомарная мутация пароля (CR-001/SEC-001) — SetPassword меняет ТОЛЬКО
        // PasswordHash: конкурентные правка профиля (fullName/email) и группа
        // студента не откатываются устаревшим снимком. Затем гашение ВСЕХ
        // reset-токенов пользователя и отзыв ВСЕХ его refresh-токенов (FR-014).
        if (!_users.SetPassword(user.Id, _hasher.Hash(password, KdfCallers.ResetPassword)))
        {
            // Защитная ветка IF-015 NOT_FOUND (зеркало MePasswordController/ProfileController):
            // запись исчезла между проверкой (3) и мутацией — пароль не применён, 401
            // «Не авторизован» БЕЗ гашения токена, отзыва refresh-токенов и событий
            // (сброс не состоялся; IF-016 — запись только при фактическом выполнении).
            // Ветвь недостижима при in-memory реализации: операции удаления
            // пользователя в API нет (ADR-043), защита — будущим реализациям
            // хранилища. Деривация Hash уже выполнена как аргумент мутатора, поэтому
            // Δkdf(reset_password)=1 и здесь (порядок Hash → SetPassword, IF-008).
            return UnauthorizedEnvelope();
        }

        _securityTokens.ConsumeAllForUser(user.Id);
        _securityTokens.RevokeAllForUserExcept(user.Id, exceptTokenHash: null);

        // Факты безопасности (IF-016/ADR-034) — после фактического сброса пароля
        // и отзыва, до ответа 204; значения/хэши токенов не логируются (NFR-006).
        _securityEvents.LogPasswordReset();
        _securityEvents.LogRefreshTokenRevoked(SecurityEventReasons.PasswordReset);

        return NoContent();
    }

    // ------------------------------------------------------------------
    // Валидация и конверт отказов (IF-001)
    // ------------------------------------------------------------------

    /// <summary>
    /// Правила confirmPassword (FR-006, зеркало repeatPassword регистрации и
    /// confirmPassword /me/password): поле обязательно (пустое и пробельное —
    /// required); дальше — дословное равенство без трима.
    /// </summary>
    private static IReadOnlyList<string> ConfirmPasswordErrors(string password, string confirm)
    {
        if (string.IsNullOrWhiteSpace(confirm))
        {
            return [ValidationTexts.Required];
        }

        return FieldValidators.PasswordMatch(password, confirm);
    }

    /// <summary>Накопление полевых ошибок: пустой перечень поле не создаёт.</summary>
    private static void Collect(Dictionary<string, string[]> errors, string field, IReadOnlyList<string> texts)
    {
        if (texts.Count > 0)
        {
            errors[field] = [.. texts];
        }
    }

    /// <summary>400 CODE_REJECTED: единый текст без раскрытия существования (IF-008).</summary>
    private static IActionResult CodeRejected() =>
        new BadRequestObjectResult(new ErrorEnvelope { Message = ValidationTexts.RecoveryCodeRejected })
        {
            ContentTypes = { "application/json" },
        };

    /// <summary>400 RESET_LINK_INVALID: единый текст для всех веток отказа (IF-008).</summary>
    private static IActionResult ResetLinkInvalid() =>
        new BadRequestObjectResult(new ErrorEnvelope { Message = ValidationTexts.ResetTokenInvalid })
        {
            ContentTypes = { "application/json" },
        };

    /// <summary>401 NOT_FOUND защитной ветки SetPassword: конверт без errors (IF-015).</summary>
    private static IActionResult UnauthorizedEnvelope() =>
        new ObjectResult(new ErrorEnvelope { Message = AuthCoreDefaults.UnauthorizedMessage })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
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

    private static IActionResult RateLimited() =>
        new ObjectResult(new ErrorEnvelope { Message = ValidationTexts.RateLimited })
        {
            StatusCode = StatusCodes.Status429TooManyRequests,
            ContentTypes = { "application/json" },
        };

    /// <summary>SHA-256 hex (строчные) значения reset-токена — зеркало TokenService (IF-003).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
