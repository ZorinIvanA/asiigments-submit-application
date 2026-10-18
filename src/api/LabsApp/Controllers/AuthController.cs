using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Domain;
using LabsApp.Domain.Dtos;
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
/// Контроллер аутентификации (C-005, IF-007): register/login/refresh/logout/me.
/// Доступ — анонимный (register, login, refresh, logout, FR-022); me — role=user
/// (любая аутентифицированная роль; анонимно — 401 «Не авторизован» от схемы
/// аутентификации, ADR-003).
///
/// POST /auth/register (FR-006) — фиксированный порядок:
///  1. TryAcquire регистрационного лимитера (5/3600с на IP, IF-006) ДО разбора
///     тела — 429 «Слишком много попыток. Повторите позже» (СПО раньше 400);
///  2. lenient-чтение тела (ADR-014): отсутствующие/нестроковые поля — пустые
///     строки, лишние поля (включая role) игнорируются; пакетная валидация
///     ВСЕХ полей сразу с текстами словаря (FR-023): fullName 1–200 после трима,
///     login 1–100 + charset, email формат + ≤254, password — сырая строка без
///     трима 8–128 (&gt;128 → только password.max), repeatPassword — обязателен
///     и дословно равен password; нарушение → 400 «Данные заполнены неверно»;
///  3. дубликат lower(login) → 409 «Пользователь с таким логином уже существует»
///     (проверяется РАНЬШЕ email);
///  4. дубликат lower(email) → 409 «Пользователь с таким email уже существует»;
///  5. создание User role='student' всегда, groupId=null, пароль хэшируется —
///     ровно одна деривация (метка register) ТОЛЬКО здесь, выпуск токенов и
///     обеих cookie (FR-008), ответ 201 + MeDto {login, fullName, role,
///     groupName=null}.
///
/// POST /auth/login (FR-007) — фиксированный порядок (SEC-001): битый JSON →
/// 400 «Данные заполнены неверно» БЕЗ KDF; login триммится для поиска
/// (ci-правило репозитория), password — как есть; поиск по lower(login);
/// РОВНО одна деривация — Verify при найденном, VerifyReference при ненайденном
/// (пустой кандидат замещается односимвольной строкой: хэшер отклоняет пустой
/// пароль без деривации, а гейт FR-027 требует Δkdf=1 и в ветке «{} без полей»);
/// успех → 200 MeDto + обе cookie, лимитер НЕ опрашивается и метку не пишет;
/// отказ → ShouldBlock('lower(trim(login))|IP'): true → 429 без метки (уже
/// после KDF — тайминг 429 неотличим от 401), false → метка + 401 «Неверный
/// логин или пароль» (текст един для неверного пароля, неизвестного логина и
/// пустых полей).
///
/// POST /auth/refresh (FR-009): по refresh-cookie; отсутствует/неизвестен
/// (нет записи по SHA-256)/просрочен (expiresAt ≤ now)/отозван (revokedAt ≠
/// null) → 401 «Не авторизован» без Set-Cookie; валиден → НОВАЯ access-cookie,
/// refresh не переустанавливается и не ротируется, ответ 204.
///
/// POST /auth/logout (FR-010): идемпотентен и БЕЗ требования валидного access
/// (истёкший access + живой refresh → 204 и отзыв); живой предъявленный
/// refresh отзывается по SHA-256; в любом случае обе cookie очищаются
/// (Max-Age=0); ответ всегда 204.
///
/// GET /auth/me (FR-011): 200 MeDto; groupName — по ТЕКУЩЕМУ состоянию групп
/// (зеркало мока toProfileDto); пользователь, удалённый из хранилища при живом
/// access-токене, даёт 401 «Не авторизован».
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IUserRepository users,
    IGroupRepository groups,
    IPasswordHasher hasher,
    ITokenService tokens,
    ICookieService cookies,
    ISecurityTokenRepository securityTokens,
    ISecurityEventLogger securityEvents,
    RegisterLimiter registerLimiter,
    LoginFailureLimiter loginFailures,
    IClientIpResolver clientIp,
    TimeProvider timeProvider) : ControllerBase
{
    private const string FullNameField = "fullName";
    private const string LoginField = "login";
    private const string EmailField = "email";
    private const string PasswordField = "password";
    private const string RepeatPasswordField = "repeatPassword";

    /// <summary>
    /// FR-007/FR-027: пустой кандидат пароля обязан пройти ветку проверки с той
    /// же стоимостью (Δkdf=1 и при «{} без полей»), а хэшер отклоняет пустой
    /// пароль без деривации (IF-002) — контроллер подставляет односимвольную
    /// строку, проверка которой заведомо неуспешна.
    /// </summary>
    private const string EmptyPasswordStandIn = " ";

    private readonly IUserRepository _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly IGroupRepository _groups = groups ?? throw new ArgumentNullException(nameof(groups));
    private readonly IPasswordHasher _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
    private readonly ITokenService _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private readonly ICookieService _cookies = cookies ?? throw new ArgumentNullException(nameof(cookies));
    private readonly ISecurityTokenRepository _securityTokens =
        securityTokens ?? throw new ArgumentNullException(nameof(securityTokens));
    private readonly ISecurityEventLogger _securityEvents =
        securityEvents ?? throw new ArgumentNullException(nameof(securityEvents));
    private readonly RegisterLimiter _registerLimiter =
        registerLimiter ?? throw new ArgumentNullException(nameof(registerLimiter));
    private readonly LoginFailureLimiter _loginFailures =
        loginFailures ?? throw new ArgumentNullException(nameof(loginFailures));
    private readonly IClientIpResolver _clientIp = clientIp ?? throw new ArgumentNullException(nameof(clientIp));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    // ------------------------------------------------------------------
    // POST /auth/register (FR-006)
    // ------------------------------------------------------------------

    /// <summary>POST /api/v1/auth/register — регистрация студента (201 MeDto + обе cookie).</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(CancellationToken cancellationToken)
    {
        // (1) Лимитер регистраций ДО разбора тела (IF-006): считаются ВСЕ попытки.
        if (!_registerLimiter.TryAcquire(_clientIp.GetClientIp(HttpContext)))
        {
            return RateLimited();
        }

        // (2) Lenient-чтение + пакетная валидация — ВСЕ ошибки сразу (FR-006).
        var body = await LenientBodyReader.FromStreamAsync(Request.Body, cancellationToken);
        var fullName = body.GetStringOrNull(FullNameField) ?? string.Empty;
        var login = body.GetStringOrNull(LoginField) ?? string.Empty;
        var email = body.GetStringOrNull(EmailField) ?? string.Empty;
        var password = body.GetStringOrNull(PasswordField) ?? string.Empty;
        var repeatPassword = body.GetStringOrNull(RepeatPasswordField) ?? string.Empty;

        var errors = new Dictionary<string, string[]>();
        Collect(errors, FullNameField, FieldValidators.FullName(fullName));
        Collect(errors, LoginField, FieldValidators.Login(login));
        Collect(errors, EmailField, FieldValidators.Email(email));
        Collect(errors, PasswordField, FieldValidators.Password(password));
        Collect(errors, RepeatPasswordField, RepeatPasswordErrors(password, repeatPassword));
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        var trimmedLogin = login.Trim();
        var trimmedEmail = email.Trim();

        // (3) Дубликат lower(login) — РАНЬШЕ email (FR-006); ci-правило репозитория.
        if (_users.GetByLogin(trimmedLogin) is not null)
        {
            return Duplicate(ValidationTexts.DuplicateLogin);
        }

        // (4) Дубликат lower(email).
        if (_users.GetByEmail(trimmedEmail) is not null)
        {
            return Duplicate(ValidationTexts.DuplicateEmail);
        }

        // (5) Создание student: роль и поле role из входа игнорируются, groupId=null;
        // ровно одна деривация (метка register) — только здесь.
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = trimmedLogin,
            Email = trimmedEmail,
            PasswordHash = _hasher.Hash(password, KdfCallers.Register),
            FullName = fullName.Trim(),
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };

        try
        {
            // Атомарная ci-проверка + вставка под StorageLock (IF-015): гонка двух
            // регистраций на один login/email → 409 по исключению репозитория.
            _users.Add(user);
        }
        catch (StorageConflictException)
        {
            // Порядок конфликтов сохраняется: сначала login, потом email.
            return Duplicate(_users.GetByLogin(trimmedLogin) is not null
                ? ValidationTexts.DuplicateLogin
                : ValidationTexts.DuplicateEmail);
        }

        IssueSession(user.Id, user.Role, user.Login);
        return StatusCode(StatusCodes.Status201Created, ToMeDto(user));
    }

    // ------------------------------------------------------------------
    // POST /auth/login (FR-007)
    // ------------------------------------------------------------------

    /// <summary>POST /api/v1/auth/login — вход (200 MeDto + обе cookie; 401/429).</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(CancellationToken cancellationToken)
    {
        // (1) Разбор JSON: битое тело → 400 БЕЗ KDF (FR-007).
        var body = await LenientBodyReader.FromStreamAsync(Request.Body, cancellationToken);
        if (!body.ParseSuccess)
        {
            return BadRequestWithoutFieldErrors();
        }

        // (2) login триммится для поиска (ci — в репозитории), password — как есть;
        // отсутствующие/нестроковые поля → пустые строки (ADR-014).
        var login = body.GetStringOrNull(LoginField) ?? string.Empty;
        var password = body.GetStringOrNull(PasswordField) ?? string.Empty;
        var lookupLogin = login.Trim();

        // (3)+(4) Поиск и РОВНО одна деривация: Verify при найденном,
        // VerifyReference при ненайденном — ветки неразличимы по стоимости.
        var user = _users.GetByLogin(lookupLogin);
        var passwordCandidate = password.Length == 0 ? EmptyPasswordStandIn : password;
        var authenticated = user is not null
            ? _hasher.Verify(passwordCandidate, user.PasswordHash, KdfCallers.Login)
            : _hasher.VerifyReference(passwordCandidate);

        if (user is not null && authenticated)
        {
            // (5) Успех: токены+cookie; лимитер НЕ опрашивается и НЕ пишет метку.
            IssueSession(user.Id, user.Role, user.Login);
            return Ok(ToMeDto(user));
        }

        // (6) Отказ: ShouldBlock ПОСЛЕ выполненной KDF; блок → 429 без метки.
        var ip = _clientIp.GetClientIp(HttpContext);
        if (_loginFailures.ShouldBlock(lookupLogin, ip))
        {
            return RateLimited();
        }

        _loginFailures.RegisterFailure(lookupLogin, ip);
        return UnauthorizedCredentials();
    }

    // ------------------------------------------------------------------
    // POST /auth/refresh (FR-009)
    // ------------------------------------------------------------------

    /// <summary>POST /api/v1/auth/refresh — новый access-cookie по refresh-cookie (204; 401 без Set-Cookie).</summary>
    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        if (FindLiveRefreshRecord() is not { } record || _users.GetById(record.UserId) is not { } user)
        {
            return UnauthorizedEnvelope();
        }

        // FR-009: НОВЫЙ access-cookie; refresh не переустанавливается и не ротируется.
        // login передаётся явно (канал subject, аменда CR-001/ADR-044) — двухаргументная
        // форма IssueAccessToken в прод-коде не используется (grep-гейт ADR-044).
        _cookies.SetAccess(HttpContext, _tokens.IssueAccessToken(user.Id, user.Role, user.Login));
        return NoContent();
    }

    // ------------------------------------------------------------------
    // POST /auth/logout (FR-010)
    // ------------------------------------------------------------------

    /// <summary>POST /api/v1/auth/logout — идемпотентный выход (всегда 204 + сброс обеих cookie).</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // FR-010: валидный access не требуется; отзывается только ЖИВОЙ
        // предъявленный refresh (отсутствующий/чужой токен — отсутствие операции).
        if (FindLiveRefreshRecord() is { } live)
        {
            _securityTokens.Revoke(live.Id);
            // Факт безопасности — ТОЛЬКО при фактическом отзыве (IF-016/ADR-034):
            // идемпотентный 204 без отзыва ничего не пишет.
            _securityEvents.LogRefreshTokenRevoked(SecurityEventReasons.Logout);
        }

        _cookies.ClearAuth(HttpContext);
        return NoContent();
    }

    // ------------------------------------------------------------------
    // GET /auth/me (FR-011)
    // ------------------------------------------------------------------

    /// <summary>GET /api/v1/auth/me — MeDto текущего пользователя (любая роль, FR-022 role=user).</summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return TryLoadCurrentUser() is { } user ? Ok(ToMeDto(user)) : UnauthorizedEnvelope();
    }

    // ------------------------------------------------------------------
    // Сессии и текущий пользователь
    // ------------------------------------------------------------------

    /// <summary>
    /// Выпуск сессии (FR-008): access-JWT + opaque refresh (в хранилище — только
    /// SHA-256-хэш, TTL из конфигурации), обе cookie через ICookieService (IF-004).
    /// <paramref name="login"/> передаётся в access-JWT явно (канал subject,
    /// аменда CR-001/ADR-044): вызывающие (register/login) — всегда user.Login.
    /// </summary>
    private void IssueSession(Guid userId, string role, string? login)
    {
        var access = _tokens.IssueAccessToken(userId, role, login);
        var refresh = _tokens.CreateRefreshToken(userId);
        _securityTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = refresh.TokenHash,
            ExpiresAt = refresh.ExpiresAt,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
        _cookies.SetAccess(HttpContext, access);
        _cookies.SetRefresh(HttpContext, refresh.Value);
    }

    /// <summary>
    /// Живая refresh-сессия по cookie (FR-009/FR-010): запись по SHA-256
    /// предъявленного значения; живость (не отозван, expiresAt &gt; now)
    /// вычисляет хранилище лениво (IF-015); иначе null.
    /// </summary>
    private RefreshToken? FindLiveRefreshRecord()
    {
        var value = Request.Cookies[AuthCoreDefaults.RefreshTokenCookieName];
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return _securityTokens.FindLiveByHash(Sha256Hex(value));
    }

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

    /// <summary>MeDto: groupName — по ТЕКУЩЕМУ состоянию групп (FR-011).</summary>
    private MeDto ToMeDto(User user) => new()
    {
        Login = user.Login,
        FullName = user.FullName,
        Role = user.Role,
        GroupName = user.GroupId is { } groupId ? _groups.GetById(groupId)?.Name : null,
    };

    // ------------------------------------------------------------------
    // Валидация и конверт отказов (IF-001)
    // ------------------------------------------------------------------

    /// <summary>
    /// Правила repeatPassword (FR-006): поле обязательно (пустое и пробельное —
    /// required, зеркало requiredTrim клиента); дальше — дословное равенство
    /// без трима. Пустой password (нестроковое поле либо отсутствие, FR-006)
    /// тоже нарушает дословное равенство с непустым повтором: mismatch входит
    /// в пакет ошибок (BUG-002/TS-208), хотя FieldValidators.PasswordMatch
    /// пустые значения не сравнивает.
    /// </summary>
    private static IReadOnlyList<string> RepeatPasswordErrors(string password, string repeat)
    {
        if (string.IsNullOrWhiteSpace(repeat))
        {
            return [ValidationTexts.Required];
        }

        if (password.Length == 0)
        {
            return [ValidationTexts.PasswordMismatch];
        }

        return FieldValidators.PasswordMatch(password, repeat);
    }

    /// <summary>Накопление полевых ошибок: пустой перечень поле не создаёт.</summary>
    private static void Collect(Dictionary<string, string[]> errors, string field, IReadOnlyList<string> texts)
    {
        if (texts.Count > 0)
        {
            errors[field] = [.. texts];
        }
    }

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

    private static IActionResult UnauthorizedCredentials() =>
        new ObjectResult(new ErrorEnvelope { Message = ValidationTexts.InvalidCredentials })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/json" },
        };

    private static IActionResult Duplicate(string message) =>
        new ConflictObjectResult(new ErrorEnvelope { Message = message })
        {
            ContentTypes = { "application/json" },
        };

    private static IActionResult RateLimited() =>
        new ObjectResult(new ErrorEnvelope { Message = ValidationTexts.RateLimited })
        {
            StatusCode = StatusCodes.Status429TooManyRequests,
            ContentTypes = { "application/json" },
        };

    /// <summary>SHA-256 hex (строчные) значения refresh-токена — зеркало TokenService (IF-003).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
