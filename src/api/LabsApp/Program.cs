using System.Diagnostics.Metrics;
using System.Text.Encodings.Web;
using System.Text.Json;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Observability;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// ЗОНА DI-РЕГИСТРАЦИЙ
// ============================================================================
// Якоря вставки для последующих подзадач (сохранять расположение и порядок).
// ============================================================================

// Итерация «БД»: хранилище — PostgreSQL (EF Core + Npgsql), единственный
// провайдер приложения; тестовые двойки живут в tests/TestInfrastructure и
// подменяют регистрации в тестовых фабриках хоста. Схема создаётся
// SQL-скриптами src/api/db (без миграций EF).
var storageOptions = builder.Configuration
    .GetSection(StorageOptions.SectionName)
    .Get<StorageOptions>() ?? new StorageOptions();
builder.Services.AddPostgresStorage(storageOptions.ConnectionString);

builder.Services.AddOptions<StorageOptions>()
    .BindConfiguration(StorageOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<StorageOptions>>(
    new StorageOptionsValidator(builder.Environment.EnvironmentName));

// Единый источник бизнес-времени (tech solution «время», FR-003): все TTL, окна
// лимитеров и метки времени читают TimeProvider из DI; композиция-корень —
// единственная точка, которую тесты переопределяют FakeTimeProvider'ом (ADR-002).
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // FR-001: глобальный camelCase, идентичный полям DTO клиента.
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // API отдаёт application/json с кириллическими текстами; вложений в
        // HTML-контекст нет, поэтому не-ASCII символы не экранируются.
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Единая сериализация для minimal API (/health) и WriteAsJsonAsync в middleware.
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

// IF-001/FR-023: фреймворковые 400 (ошибки привязки/десериализации, включая
// синтаксически некорректный JSON тела) — конверт {'message':'Данные заполнена
// неверно'} без errors; errors присутствует только у полевой валидации.
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context =>
        new BadRequestObjectResult(new ErrorEnvelope { Message = ErrorTexts.InvalidData })
        {
            ContentTypes = { "application/json" },
        });

// C-011 (IF-016): Meter «labs.api» — DI-singleton; инструменты метрик
// ObservabilityMiddleware создаются на его основе и живут вместе с хостом.
builder.Services.AddSingleton(_ => new Meter(ObservabilityMiddleware.MeterName, "1.0"));

AddHostingOptions(builder);

if (builder.Environment.IsDevelopment())
{
    // FR-001: Swagger UI и OpenAPI-документ публикуются только в Development
    // (Swashbuckle зарегистрирован только здесь; вне Development /swagger* — 404).
    builder.Services.AddSwaggerGen();
}

var app = builder.Build();

// T-003 (FR-025): сид выполняется при построении приложения, до начала
// обслуживания запросов; guard Seed__TeacherPassword в Production —
// в SeedOptionsValidator (отказ старта до сида).
await app.SeedDatabaseAsync();

// ============================================================================
// ЗОНА КОНВЕЙЕРА (порядок v2.2, C-001/ADR-006)
// ============================================================================
// ObservabilityMiddleware → UseExceptionHandler (500-конверт) → Routing
// → MapControllers → GET /health → UseApiRouteNotFoundHandler (404
// {'message':'Не найдено'}) → UseSwaggerBlockedOutsideDevelopment → статики
// (nosniff/DENY) → SPA-fallback (nosniff/DENY).
// CORS, 413-гейт тела и ForwardedHeaders НЕ настраиваются (FR-001 «CORS MUST NOT
// настраиваться»; 413/невалидный HTTP — тело фреймворка до конвейера,
// ISS-016/OQ-004; IP клиента — Connection.RemoteIpAddress, ADR-006).
// ============================================================================

// C-011 (IF-016): первый middleware конвейера — request-лог /api/** и /health,
// события 403/429 и метрики; до маппинга контроллеров, чтобы финальный статус
// ответа фиксировался всегда.
app.UseObservability();

// FR-023: необработанное исключение → 500 {'message':'Внутренняя ошибка сервера'}
// без имён типов и stack trace. Регистрируется сразу после наблюдаемости:
// обёртка над всем остальным конвейером, наблюдаемость остаётся самой внешней.
app.UseExceptionHandler(static errorApp => errorApp.Run(static async context =>
{
    // ISS-016/OQ-004 (IF-001, граница конверта): инфраструктурные отклонения
    // Kestrel конверту НЕ подчиняются — отвечают телом фреймворка. Отклонение
    // «превышение лимита тела» (413, BadHttpRequestException) выбрасывается
    // при чтении тела УЖЕ внутри конвейера, а обработчик исключений net8 не
    // переправляет его серверу — поэтому граница восстанавливается здесь:
    // голый серверный статус без тела и без словарного текста, ровно как его
    // отдал бы сам Kestrel до конвейера. Прочие отклонения протокола (невалидный
    // HTTP, размер заголовков) Kestrel отбрасывает до передачи в конвейер.
    var error = context.Features
        .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (IsKestrelRequestRejection(error))
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentLength = 0;
        await context.Response.CompleteAsync();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(
        new ErrorEnvelope { Message = ErrorTexts.InternalServerError });
}));

if (app.Environment.IsDevelopment())
{
    // FR-001: Swagger UI и OpenAPI-документ — только в Development; терминальные
    // middleware, обслуживающие /swagger* до маршрутизации (вне Development
    // /swagger* перехватывает UseSwaggerBlockedOutsideDevelopment — 404-конверт).
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

app.MapControllers();

// FR-001: анонимный health-check — 200 {"status":"ok"}.
app.MapGet("/health", () => Results.Json(new { status = "ok" }));

// FR-001/FR-023: /api/** без маршрута (любой метод) → 404
// {'message':'Не найдено'} — после обработки маршрутов, до SPA-fallback.
app.UseApiRouteNotFoundHandler();

if (!app.Environment.IsDevelopment())
{
    // FR-001/FR-002 (ISS-008): /swagger/** вне Development — 404-конверт после
    // обработки маршрутов, до SPA-fallback (тело — не index.html).
    app.UseSwaggerBlockedOutsideDevelopment();
}

app.UseDefaultFiles();

// FR-002/SEC-006: каждый ответ статики несёт X-Content-Type-Options: nosniff
// и X-Frame-Options: DENY.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = static context => SecurityHeaders.Apply(context.Context.Response),
});

// FR-002: SPA-fallback — GET, путь не /api, не /health, не /swagger, не
// существующий файл → index.html 200 (с nosniff/DENY); без index.html —
// 404-конверт.
app.UseSpaFallback();

app.Run();

// ============================ Регистрации хостинга ============================

/// <summary>
/// Отклонение запроса сервером Kestrel, поднятое до границы конверта
/// (ISS-016/OQ-004): тип <c>Microsoft.AspNetCore.Server.Kestrel.Core.
/// BadHttpRequestException</c> — internal в шаред-фреймворке, поэтому
/// распознаётся по полному имени типа; базовый класс — InvalidOperationException.
/// </summary>
static bool IsKestrelRequestRejection(Exception? error) =>
    error?.GetType().FullName == "Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException";

static void AddHostingOptions(WebApplicationBuilder builder)
{
    var environmentName = builder.Environment.EnvironmentName;
    var isDevelopment = builder.Environment.IsDevelopment();

    builder.Services.AddOptions<LabsOptions>()
        .BindConfiguration(LabsOptions.SectionName)
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<LabsOptions>>(new LabsOptionsValidator());

    builder.Services.AddOptions<SeedOptions>()
        .BindConfiguration(SeedOptions.SectionName)
        .PostConfigure<ILoggerFactory>((options, loggerFactory) =>
        {
            // FR-025: некорректное значение Seed__DemoData — предупреждение в лог
            // (умолчание окружения применяется в SeedOptions.ResolveDemoData).
            if (SeedOptions.IsKnownDemoDataValue(options.DemoData))
            {
                return;
            }

            loggerFactory
                .CreateLogger(HostingLogCategories.Configuration)
                .LogWarning(
                    "Переменная {Variable} имеет значение '{Value}'; ожидается 'true' или 'false' (без учёта регистра). Используется значение по умолчанию для окружения: {Default}",
                    SeedOptions.DemoDataVariable,
                    options.DemoData ?? "<не задана>",
                    isDevelopment);
        })
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<SeedOptions>>(new SeedOptionsValidator(environmentName));
}

// Совместимость с WebApplicationFactory<Program> (ADR-010): Program обязан быть публичным.
public partial class Program { }
