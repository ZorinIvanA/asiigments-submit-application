using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text;
using LabsApp.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Stub-уровневые unit-тесты ObservabilityMiddleware (ADR-017/ADR-018, AC T-023):
/// middleware вызывается напрямую с DefaultHttpContext и подставным RequestDelegate,
/// устанавливающим нужный финальный статус/claims/path — без живого конвейера,
/// контроллеров, аутентификации и лимитеров (закрытие ISS-002). Захват записей —
/// TestLogSink и TestMeterListener тестовой инфраструктуры T-004 (переиспользуются,
/// не создаются — ISS-001).
/// </summary>
public sealed class ObservabilityMiddlewareTests : IDisposable
{
    // Уникальное имя метра на экземпляр теста: MeterListener подписывается на
    // инструменты метра данного имени по всему процессу, а Meter в .NET 8
    // освобождается по счётчику ссылок (одноимённые метры живут вместе) — общий
    // «labs.api» у параллельных тестовых классов даёт перекрёстные измерения и
    // недетерминизм. Контракт имени Production-метра закреплён отдельным тестом
    // MeterName_MatchesTestListenerDefault.
    private readonly string _meterName = $"{TestMeterListener.DefaultMeterName}.{Guid.NewGuid():N}";
    private readonly TestLogSink _sink = new();
    private readonly TestMeterListener _listener;
    private readonly Meter _meter;

    public ObservabilityMiddlewareTests()
    {
        _listener = new TestMeterListener(_meterName);
        _meter = new Meter(_meterName, "1.0");
    }

    public void Dispose()
    {
        _meter.Dispose();
        _listener.Dispose();
        ((IDisposable)_sink).Dispose();
    }

    [Fact]
    public void MeterName_MatchesTestListenerDefault() =>
        // Контракт связки (ADR-017): Program.cs регистрирует Meter с именем
        // ObservabilityMiddleware.MeterName; TestMeterListener (T-004) по
        // умолчанию подписывается на метр именно с этим именем.
        Assert.Equal(TestMeterListener.DefaultMeterName, ObservabilityMiddleware.MeterName);

    [Fact]
    public async Task ApiRequest_OnApiPath_LogsWithoutQueryAndRecordsHistogram()
    {
        await InvokeAsync("GET", "/api/v1/labs", StatusCodes.Status200OK, context =>
            context.Request.QueryString = new QueryString("?x=1"));

        var record = SingleRecord(ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("GET", record.State["Method"]);
        Assert.Equal("/api/v1/labs", record.State["Path"]);
        Assert.Equal(200, (int)record.State["Status"]!);
        Assert.True((double)record.State["DurationMs"]! >= 0);
        Assert.False(string.IsNullOrWhiteSpace((string)record.State["TraceId"]!));
        Assert.Equal(
            1,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "/api/v1/labs"), ("method", "GET"), ("status", 200)));
    }

    [Fact]
    public async Task RouteTag_WithRouteEndpoint_UsesTemplateInsteadOfConcretePath()
    {
        // CR-001 (ADR-017: route — «шаблон пути без query»): маршруты с параметрами
        // дают одну серию на шаблон, а не на каждый id — кардинальность не растёт
        // с числом сущностей. Endpoint доступен после _next (маршрутизация внутри).
        await InvokeAsync("GET", "/api/v1/labs/42", StatusCodes.Status200OK, context =>
        {
            context.Request.QueryString = new QueryString("?page=2");
            SetRouteTemplate(context, "/api/v1/labs/{id}");
        });

        // Журнал — конкретный path без query; метрика — шаблон.
        var record = SingleRecord(ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal("/api/v1/labs/42", record.State["Path"]);
        Assert.Equal(
            1,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "/api/v1/labs/{id}"), ("method", "GET"), ("status", 200)));
        // Серия по конкретному пути не создаётся — свёрнута в шаблон.
        Assert.Equal(
            0,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "/api/v1/labs/42"), ("method", "GET"), ("status", 200)));
    }

    [Fact]
    public async Task RouteTag_TemplateWithoutLeadingSlash_NormalizedWithSlash()
    {
        // Атрибутные маршруты контроллеров дают RawText без ведущего слэша —
        // метка приводится к единому виду с MapGet-маршрутами (например, "/health").
        await InvokeAsync("GET", "/api/v1/students/7/group", StatusCodes.Status200OK, context =>
            SetRouteTemplate(context, "api/v1/students/{id}/group"));

        Assert.Equal(
            1,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "/api/v1/students/{id}/group"), ("method", "GET"), ("status", 200)));
    }

    [Fact]
    public async Task RouteTag_WithoutEndpoint_FallsBackToConcretePath()
    {
        // Путь без маршрута (404, endpoint не задан) — в route остаётся
        // конкретный path (fix_direction CR-001).
        await InvokeAsync("GET", "/api/v1/unknown", StatusCodes.Status404NotFound);

        var record = SingleRecord(ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal("/api/v1/unknown", record.State["Path"]);
        Assert.Equal(
            1,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "/api/v1/unknown"), ("method", "GET"), ("status", 404)));
    }

    [Fact]
    public async Task RouteTag_NonRouteEndpoint_FallsBackToDisplayName()
    {
        // Endpoint без RoutePattern (не RouteEndpoint) — fallback на DisplayName.
        await InvokeAsync("GET", "/api/v1/custom", StatusCodes.Status200OK, context =>
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                EndpointMetadataCollection.Empty,
                "LabsApp.Stub.CustomEndpoint")));

        Assert.Equal(
            1,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "LabsApp.Stub.CustomEndpoint"), ("method", "GET"), ("status", 200)));
    }

    [Fact]
    public async Task Forbidden403_WithAuthenticatedSubject_LogsSecurityWithSubjectAndCounter()
    {
        await InvokeAsync("GET", "/api/v1/labs", StatusCodes.Status403Forbidden, context =>
        {
            context.Request.QueryString = new QueryString("?page=1");
            context.User = Authenticated("teacher01");
        });

        var record = SingleRecord(ObservabilityMiddleware.SecurityLogCategory);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(403, (int)record.State["Status"]!);
        Assert.Equal("/api/v1/labs", record.State["Path"]);
        Assert.Equal("teacher01", record.State["Subject"]);
        Assert.False(string.IsNullOrWhiteSpace((string)record.State["TraceId"]!));
        Assert.Equal(1, _listener.CounterIncrement("security_rejections_total", ("status", 403)));

        // 403 на /api-путь — это всё ещё завершённый API-запрос: Api.Request тоже есть.
        Assert.Contains(
            _sink.Snapshot(),
            item => item.Category == ObservabilityMiddleware.RequestLogCategory);
    }

    [Fact]
    public async Task TooManyRequests429_Anonymous_LogsSecurityWithoutSubject()
    {
        await InvokeAsync("POST", "/api/v1/auth/login", StatusCodes.Status429TooManyRequests, context =>
            context.User = new ClaimsPrincipal(new ClaimsIdentity()));

        var record = SingleRecord(ObservabilityMiddleware.SecurityLogCategory);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(429, (int)record.State["Status"]!);
        Assert.False(record.State.ContainsKey("Subject"));
        Assert.Equal(1, _listener.CounterIncrement("security_rejections_total", ("status", 429)));
    }

    [Fact]
    public async Task Forbidden403_AuthenticatedWithoutLoginClaim_SubjectOmitted()
    {
        // NFR-008 «login, если известен»: аутентифицированный пользователь без
        // claim login — запись без субъекта.
        await InvokeAsync("GET", "/api/v1/labs", StatusCodes.Status403Forbidden, context =>
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim("sub", "1") }, "TestAuth")));

        var record = SingleRecord(ObservabilityMiddleware.SecurityLogCategory);
        Assert.False(record.State.ContainsKey("Subject"));
    }

    public static TheoryData<string, string, int, string, string, string> DerivedEventsByRouteAndStatus() => new()
    {
        // method, path, финальный статус, инструмент, метка, значение метки.
        { "POST", "/api/v1/auth/login", 200, "auth_login_attempts_total", "result", "login_success" },
        { "POST", "/api/v1/auth/login", 401, "auth_login_attempts_total", "result", "login_failure" },
        { "POST", "/api/v1/auth/login", 429, "auth_login_attempts_total", "result", "rate_limited" },
        { "POST", "/api/v1/auth/logout", 204, "security_audit_events_total", "op", "logout" },
        { "PUT", "/api/v1/me/password", 204, "security_audit_events_total", "op", "password_change" },
        { "POST", "/api/v1/auth/reset-password", 204, "security_audit_events_total", "op", "password_reset" },
        { "POST", "/api/v1/auth/recovery/request", 200, "security_audit_events_total", "op", "recovery_code_requested" },
    };

    [Theory]
    [MemberData(nameof(DerivedEventsByRouteAndStatus))]
    public async Task DerivedEvent_ByRouteMethodAndStatus_Recorded(
        string method,
        string path,
        int finalStatus,
        string instrument,
        string tagKey,
        string tagValue)
    {
        await InvokeAsync(method, path, finalStatus);

        Assert.Equal(1, _listener.CounterIncrement(instrument, (tagKey, tagValue)));
    }

    [Fact]
    public async Task RecoveryRequest_Rejected429_NotCountedInAuditOps()
    {
        // ADR-017: отклонённый 429 запрос кода в op не попадает — он учтён
        // в security_rejections_total.
        await InvokeAsync("POST", "/api/v1/auth/recovery/request", StatusCodes.Status429TooManyRequests);

        foreach (var op in new[]
                 {
                     "login_success",
                     "login_failure",
                     "logout",
                     "password_change",
                     "password_reset",
                     "recovery_code_requested",
                 })
        {
            Assert.Equal(0, _listener.CounterIncrement("security_audit_events_total", ("op", op)));
        }

        Assert.Equal(0, _listener.CounterIncrement("auth_login_attempts_total", ("result", "rate_limited")));
        Assert.Equal(1, _listener.CounterIncrement("security_rejections_total", ("status", 429)));
    }

    [Fact]
    public async Task LoginPath_WrongMethod_NoDerivedEvent()
    {
        // Граница таблицы: совпадать должны и маршрут, и метод.
        await InvokeAsync("GET", "/api/v1/auth/login", StatusCodes.Status200OK);

        Assert.Equal(0, _listener.CounterIncrement("auth_login_attempts_total", ("result", "login_success")));
        Assert.Equal(0, _listener.CounterIncrement("security_audit_events_total", ("op", "login_success")));
    }

    [Fact]
    public async Task Logout_UnexpectedStatus_NoAuditEvent()
    {
        // Граница таблицы: logout фиксируется только с финальным 204.
        await InvokeAsync("POST", "/api/v1/auth/logout", StatusCodes.Status200OK);

        Assert.Equal(0, _listener.CounterIncrement("security_audit_events_total", ("op", "logout")));
    }

    [Fact]
    public async Task ChangePassword_Status200_NoAuditEvent()
    {
        // ADR-034/T-125 (исправление мёртвого предиката): PUT /me/password
        // отвечает 204 — успех со статусом 200 невозможен и в op не попадает.
        await InvokeAsync("PUT", "/api/v1/me/password", StatusCodes.Status200OK);

        Assert.Equal(0, _listener.CounterIncrement("security_audit_events_total", ("op", "password_change")));
    }

    [Fact]
    public async Task ResetPassword_Status200_NoAuditEvent()
    {
        // ADR-034/T-125 (исправление мёртвого предиката): POST /reset-password
        // отвечает 204 — успех со статусом 200 невозможен и в op не попадает.
        await InvokeAsync("POST", "/api/v1/auth/reset-password", StatusCodes.Status200OK);

        Assert.Equal(0, _listener.CounterIncrement("security_audit_events_total", ("op", "password_reset")));
    }

    [Fact]
    public async Task LoginPath_StatusOutsideTable_NoLoginAttemptEvent()
    {
        // Граница таблицы: login со статусом вне {200, 401, 429} не порождает
        // событие auth_login_attempts_total.
        await InvokeAsync("POST", "/api/v1/auth/login", StatusCodes.Status500InternalServerError);

        foreach (var result in new[] { "login_success", "login_failure", "rate_limited" })
        {
            Assert.Equal(0, _listener.CounterIncrement("auth_login_attempts_total", ("result", result)));
        }

        Assert.Equal(0, _listener.CounterIncrement("security_audit_events_total", ("op", "login_success")));
    }

    [Fact]
    public async Task RouteTag_RouteEndpointWithoutRawText_FallsBackToDisplayName()
    {
        // Граница: RouteEndpoint с RoutePattern без RawText (собранным из сегментов,
        // а не разобранным из строки) — fallback на DisplayName, не на конкретный path.
        await InvokeAsync("GET", "/api/v1/segment-built", StatusCodes.Status200OK, context =>
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Pattern(RoutePatternFactory.Segment(RoutePatternFactory.LiteralPart("stub"))),
                order: 0,
                EndpointMetadataCollection.Empty,
                displayName: "LabsApp.Stub.SegmentEndpoint")));

        Assert.Equal(
            1,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "LabsApp.Stub.SegmentEndpoint"), ("method", "GET"), ("status", 200)));
    }

    [Fact]
    public async Task HealthRequest_RecordsRequestLogAndBothHistograms()
    {
        await InvokeAsync("GET", "/health", StatusCodes.Status200OK);

        var record = SingleRecord(ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal("GET", record.State["Method"]);
        Assert.Equal("/health", record.State["Path"]);
        Assert.Equal(1, _listener.MeasurementCount("health_duration_ms"));
        Assert.Equal(
            1,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "/health"), ("method", "GET"), ("status", 200)));
    }

    [Fact]
    public async Task NonApiPath_NoRequestLog()
    {
        await InvokeAsync("GET", "/works", StatusCodes.Status200OK);

        Assert.DoesNotContain(
            _sink.Snapshot(),
            record => record.Category == ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal(
            0,
            _listener.MeasurementCount(
                "api_request_duration_ms",
                ("route", "/works"), ("method", "GET"), ("status", 200)));
    }

    [Fact]
    public async Task ApiPrefixButNotSegment_NoRequestLog()
    {
        // Граница «/api/**»: /apifoo не является путём /api (граница сегмента).
        await InvokeAsync("GET", "/apifoo", StatusCodes.Status200OK);

        Assert.DoesNotContain(
            _sink.Snapshot(),
            record => record.Category == ObservabilityMiddleware.RequestLogCategory);
    }

    [Fact]
    public async Task Status401_IsNotSecurityRejection()
    {
        // Api.Security — только для финальных 403/429; 401 в него не попадает.
        await InvokeAsync("GET", "/api/v1/labs", StatusCodes.Status401Unauthorized);

        Assert.DoesNotContain(
            _sink.Snapshot(),
            record => record.Category == ObservabilityMiddleware.SecurityLogCategory);
        Assert.Equal(0, _listener.CounterIncrement("security_rejections_total", ("status", 401)));
    }

    public static TheoryData<int> Nfr006FinalStatuses() => new()
    {
        // NFR-006 v2.3 (AC T-003 «Один запрос — одна запись»): обязательные
        // финальные статусы — успех, неаутентифицирован, отказ доступа,
        // нет маршрута, лимитер, 413-граница тела.
        StatusCodes.Status200OK,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status429TooManyRequests,
        StatusCodes.Status413PayloadTooLarge,
    };

    [Theory]
    [MemberData(nameof(Nfr006FinalStatuses))]
    public async Task ApiRequest_EveryFinalStatus_ExactlyOneRequestRecord(int finalStatus)
    {
        // Ровно одна запись Api.Request на любой финальный статус запроса /api/**;
        // состав полей фиксирован: method, path без query, status, durationMs, traceId.
        await InvokeAsync("GET", "/api/v1/labs", finalStatus);

        var record = SingleRecord(ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal("GET", record.State["Method"]);
        Assert.Equal("/api/v1/labs", record.State["Path"]);
        Assert.Equal(finalStatus, (int)record.State["Status"]!);
        Assert.True((double)record.State["DurationMs"]! >= 0);
        Assert.False(string.IsNullOrWhiteSpace((string)record.State["TraceId"]!));
    }

    [Fact]
    public async Task ApiRequest_SerializedRecord_ContainsNoNfr006MarkersOrBodyFragments()
    {
        // NFR-006 (AC T-003 «Маркеры отсутствуют»): во входе запроса размещены все
        // маркеры (пароль, currentPassword, resetToken, значения cookie access и
        // refresh) и тело; сериализованная запись Api.Request не содержит ни одного
        // из них и следов query/тела — состав полей фиксирован контрактом.
        await InvokeAsync("POST", "/api/v1/auth/login", StatusCodes.Status401Unauthorized, context =>
        {
            context.Request.QueryString = new QueryString(
                "?password=секрет-пароль&currentPassword=секрет-старый&resetToken=секрет-токен");
            context.Request.Headers.Cookie =
                "access_token=секрет-access; refresh_token=секрет-refresh";
            context.Request.ContentType = "application/json";
            context.Request.Headers.ContentLength = 64;
            context.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes("[{\"password\":\"секрет-пароль\"}]"));
        });

        var serialized = SerializeForMarkerCheck(SingleRecord(ObservabilityMiddleware.RequestLogCategory));

        Assert.DoesNotContain("пароль", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currentPassword", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resetToken", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("секрет", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordsContainNoQueryOrHeaderValues()
    {
        await InvokeAsync("GET", "/api/v1/labs", StatusCodes.Status200OK, context =>
        {
            context.Request.QueryString = new QueryString("?access_token=SECRET-TOKEN&fullName=Иванов");
            context.Request.Headers.Authorization = "Bearer SECRET-JWT";
        });
        await InvokeAsync("GET", "/api/v1/admin", StatusCodes.Status403Forbidden, context =>
        {
            context.Request.QueryString = new QueryString("?code=123456");
            context.User = Authenticated("teacher01");
        });

        var records = _sink.Snapshot();
        Assert.Equal(3, records.Count);

        foreach (var record in records)
        {
            Assert.DoesNotContain("SECRET", record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET", record.MessageTemplate, StringComparison.Ordinal);
            Assert.DoesNotContain("?", record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Иванов", record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("123456", record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization", record.Message, StringComparison.Ordinal);
            Assert.All(record.State, pair => Assert.False(
                pair.Value is string value && value.Contains("SECRET", StringComparison.Ordinal),
                $"Запись {record.Category} содержит токен: {pair.Key}"));
        }

        Assert.All(
            records.Where(record => record.Category == ObservabilityMiddleware.RequestLogCategory),
            record => Assert.DoesNotContain(
                "?", (string)record.State["Path"]!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TraceId_TakenFromCurrentActivity_WhenPresent()
    {
        using var activity = new Activity("stub").Start();

        await InvokeAsync("GET", "/health", StatusCodes.Status200OK);

        var record = SingleRecord(ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal(activity.TraceId.ToString(), record.State["TraceId"]);
    }

    private async Task InvokeAsync(
        string method,
        string path,
        int finalStatus,
        Action<DefaultHttpContext>? setup = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        setup?.Invoke(context);

        var middleware = new ObservabilityMiddleware(
            requestContext =>
            {
                requestContext.Response.StatusCode = finalStatus;
                return Task.CompletedTask;
            },
            LoggerFactory.Create(builder => builder.AddProvider(_sink)),
            _meter);

        await middleware.InvokeAsync(context);
    }

    private TestLogRecord SingleRecord(string category) =>
        Assert.Single(_sink.Snapshot(), record => record.Category == category);

    /// <summary>
    /// Сериализует запись так, как это сделал бы реальный sink: категория, уровень,
    /// отформатированное сообщение, шаблон и все пары структурированного состояния —
    /// поверхность substring-маркерной проверки NFR-006.
    /// </summary>
    private static string SerializeForMarkerCheck(TestLogRecord record) =>
        string.Join(
            "|",
            new[]
            {
                record.Category,
                record.Level.ToString(),
                record.Message,
                record.MessageTemplate ?? string.Empty,
            }.Concat(record.State.Select(pair => $"{pair.Key}={pair.Value}")));

    /// <summary>
    /// Имитация завершённой маршрутизации: назначает контексту RouteEndpoint с
    /// заданным шаблоном (middleware читает endpoint только после _next, как в
    /// реальном конвейере после UseRouting).
    /// </summary>
    private static void SetRouteTemplate(DefaultHttpContext context, string template) =>
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(template),
            order: 0,
            EndpointMetadataCollection.Empty,
            displayName: "stub-endpoint"));

    private static ClaimsPrincipal Authenticated(string login) =>
        new(new ClaimsIdentity(
            new[] { new Claim("sub", "1"), new Claim("login", login) },
            authenticationType: "TestAuth"));
}
