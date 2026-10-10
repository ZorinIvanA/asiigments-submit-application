using System.Diagnostics;
using System.Security.Claims;
using LabsApp.Auth;
using LabsApp.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Stub-уровневые unit-тесты SecurityEventLogger (IF-016, аменда CR-002/ADR-034,
/// T-125): логгер вызывается напрямую с подставным IHttpContextAccessor (свойство
/// HttpContext назначается тестом), записи захватываются TestLogSink тестовой
/// инфраструктуры T-004 (переиспользуется — ISS-001). Проверяются: единая
/// категория «Api.Security» (унификация с ObservabilityMiddleware), уровень
/// Warning, состав записи (факт/reason, traceId, subject если известен) и
/// маркерная проверка NFR-006 — секреты в записях отсутствуют.
/// </summary>
public sealed class SecurityEventLoggerTests : IDisposable
{
    private const string SecretOldPassword = "секрет-старый-пароль";
    private const string SecretNewPassword = "секрет-новый-пароль";
    private const string SecretRefreshValue = "секрет-refresh-токен";

    private readonly TestLogSink _sink = new();

    public void Dispose() => ((IDisposable)_sink).Dispose();

    private SecurityEventLogger CreateLogger(HttpContext? context = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new SecurityEventLogger(
            LoggerFactory.Create(builder => builder.AddProvider(_sink)),
            accessor);
    }

    private static DefaultHttpContext AuthenticatedContext(string login) => new()
    {
        User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("login", login)],
                authenticationType: AuthCoreDefaults.AuthenticationScheme)),
        TraceIdentifier = "trace-identifier-42",
    };

    [Fact]
    public void LogCategory_IsUnifiedWithMiddlewareSecurityCategory()
    {
        // ADR-034 (AC «Категория едина»): одиночная константа — «Api.Security»,
        // отдельная «Security» упразднена.
        Assert.Equal("Api.Security", SecurityEventLogger.LogCategory);
        Assert.Equal(ObservabilityMiddleware.SecurityLogCategory, SecurityEventLogger.LogCategory);
    }

    [Fact]
    public void LogRefreshTokenRevoked_WritesWarningWithReasonAndTraceId()
    {
        var logger = CreateLogger(AuthenticatedContext("revoked-user"));

        logger.LogRefreshTokenRevoked(SecurityEventReasons.Logout);

        var record = SingleSecurityRecord();
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(SecurityEventLogger.LogCategory, record.Category);
        Assert.Equal(SecurityEventReasons.Logout, record.State["Reason"]);
        Assert.Equal("trace-identifier-42", record.State["TraceId"]);
        Assert.Equal("revoked-user", record.State["Subject"]);
    }

    [Fact]
    public void LogRefreshTokenRevoked_AnonymousContext_OmitsSubject()
    {
        // FR-010: logout анонимен — «logout допускает отсутствие subject» (IF-016).
        var logger = CreateLogger(new DefaultHttpContext());

        logger.LogRefreshTokenRevoked(SecurityEventReasons.Logout);

        var record = SingleSecurityRecord();
        Assert.False(record.State.ContainsKey("Subject"));
        Assert.False(string.IsNullOrWhiteSpace((string)record.State["TraceId"]!));
    }

    [Theory]
    [InlineData(SecurityEventReasons.Logout)]
    [InlineData(SecurityEventReasons.PasswordChange)]
    [InlineData(SecurityEventReasons.PasswordReset)]
    public void LogRefreshTokenRevoked_AllContractReasons_ReasonStateEqualsInput(string reason)
    {
        var logger = CreateLogger(new DefaultHttpContext());

        logger.LogRefreshTokenRevoked(reason);

        Assert.Equal(reason, SingleSecurityRecord().State["Reason"]);
    }

    [Fact]
    public void LogRefreshTokenRevoked_NullReason_Throws()
    {
        var logger = CreateLogger(new DefaultHttpContext());

        Assert.Throws<ArgumentNullException>(() => logger.LogRefreshTokenRevoked(null!));
    }

    [Fact]
    public void LogPasswordChanged_WritesWarningInSecurityCategory()
    {
        var logger = CreateLogger(AuthenticatedContext("changed-user"));

        logger.LogPasswordChanged();

        var record = SingleSecurityRecord();
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(ObservabilityMiddleware.SecurityLogCategory, record.Category);
        Assert.Equal("Смена пароля", record.State["Fact"]);
        Assert.Equal("changed-user", record.State["Subject"]);
    }

    [Fact]
    public void LogPasswordReset_WritesWarningInSecurityCategory()
    {
        var logger = CreateLogger(AuthenticatedContext("reset-user"));

        logger.LogPasswordReset();

        var record = SingleSecurityRecord();
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(ObservabilityMiddleware.SecurityLogCategory, record.Category);
        Assert.Equal("Сброс пароля", record.State["Fact"]);
    }

    [Fact]
    public void TraceId_TakenFromCurrentActivity_WhenPresent()
    {
        // Зеркало ObservabilityMiddleware: Activity.Current приоритетнее
        // TraceIdentifier запроса (correlation_id = Activity.Current?.TraceId).
        using var activity = new Activity("stub").Start();
        var logger = CreateLogger(AuthenticatedContext("activity-user"));

        logger.LogPasswordChanged();

        Assert.Equal(activity.TraceId.ToString(), SingleSecurityRecord().State["TraceId"]);
    }

    [Fact]
    public void OutsideHttpContext_StillWritesRecord()
    {
        // Граница: вне запроса (HttpContext отсутствует) запись не теряется —
        // traceId пуст, subject опущен.
        var logger = CreateLogger(context: null);

        logger.LogPasswordChanged();

        var record = SingleSecurityRecord();
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(string.Empty, (string)record.State["TraceId"]!);
        Assert.False(record.State.ContainsKey("Subject"));
    }

    [Fact]
    public void Records_ContainNoSecretMarkers()
    {
        // NFR-006 (маркерная проверка T-125): пароли, значения cookie и refresh-
        // токенов, размещённые в контексте запроса, не появляются ни в одной
        // записи безопасности — ни в сообщении, ни в шаблоне, ни в состоянии.
        var context = AuthenticatedContext("marker-user");
        context.Request.Headers.Cookie =
            $"access_token=секрет-access; {AuthCoreDefaults.RefreshTokenCookieName}={SecretRefreshValue}";
        var logger = CreateLogger(context);

        logger.LogPasswordChanged();
        logger.LogRefreshTokenRevoked(SecurityEventReasons.PasswordChange);
        logger.LogPasswordReset();

        var records = _sink.Snapshot();
        Assert.Equal(3, records.Count);
        foreach (var record in records)
        {
            Assert.DoesNotContain(SecretOldPassword, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretNewPassword, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretRefreshValue, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("секрет", record.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SecretRefreshValue, record.MessageTemplate ?? string.Empty, StringComparison.Ordinal);
            Assert.All(record.State, pair => Assert.False(
                pair.Value is string value && value.Contains("секрет", StringComparison.Ordinal),
                $"Запись {record.Category} содержит секрет: {pair.Key}"));
        }
    }

    private TestLogRecord SingleSecurityRecord() =>
        Assert.Single(_sink.Snapshot(), record => record.Category == SecurityEventLogger.LogCategory);
}
