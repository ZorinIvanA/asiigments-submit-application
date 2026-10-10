using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LabsApp.Hosting;
using LabsApp.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Интеграционный гейт хостинга и единого конверта ошибок (T-112, C-001;
/// FR-001, FR-002, FR-023, FR-027):
///  — полная матрица SPA-fallback (deep-link, корень, реальный файл, favicon,
///    отсутствующий index.html, /api и /swagger не перехватываются) с проверкой
///    nosniff/DENY на каждом успешном ответе статики/fallback (SEC-006);
///  — /health: анонимно, точное тело, время ответа &lt; 100 мс (Stopwatch);
///  — конверт FR-023: 400-форма (errors-карта только у полевой валидации),
///    404 несопоставленного маршрута под /api, 500 при инжектированном сбое —
///    сервис заменён реализацией, бросающей исключение (без имён типов и
///    stack trace);
///  — аудит проекта: TargetFramework net8.0 и 0 вхождений
///    EntityFrameworkCore/Npgsql в исходниках приложения (FR-001/FR-024).
/// Доменные сценарии контроллеров в гейт не входят (граница T-112); для
/// 400-формы используется анонимная полевая валидация POST /auth/register.
/// </summary>
public sealed class HostingIntegrationGateTests
{
    private const string NotFoundEnvelope = $"{{\"message\":\"{ErrorTexts.NotFound}\"}}";
    private const string InvalidDataEnvelope = $"{{\"message\":\"{ErrorTexts.InvalidData}\"}}";
    private const string InternalErrorEnvelope = $"{{\"message\":\"{ErrorTexts.InternalServerError}\"}}";
    private const string UnauthorizedEnvelope = "{\"message\":\"Не авторизован\"}";
    private const string HealthBody = "{\"status\":\"ok\"}";

    // Поля errors-карты 400-формы POST /auth/register на теле «{}» (все поля
    // пустые — каждая полевая проверка даёт хотя бы одну ошибку).
    private static readonly string[] RegisterErrorFields =
        ["fullName", "login", "email", "password", "repeatPassword"];

    // ------------------------------------------------------------------
    // SPA-fallback-матрица (FR-002) с защитными заголовками (SEC-006).
    // ------------------------------------------------------------------

    // Deep-link и корень (через UseDefaultFiles) отдают index.html с nosniff/DENY.
    [Theory]
    [InlineData("/works")]
    [InlineData("/groups/abc")]
    [InlineData("/")]
    public async Task SpaFallback_DeepLink_ReturnsIndexHtmlWithSecurityHeaders(string path)
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(indexHtml, await response.Content.ReadAsStringAsync());
        Assert.Equal(SecurityHeaders.Nosniff, response.Headers.GetValues(SecurityHeaders.XContentTypeOptions).Single());
        Assert.Equal(SecurityHeaders.Deny, response.Headers.GetValues(SecurityHeaders.XFrameOptions).Single());
    }

    // Реальный файл отдаётся как файл (не index.html) с nosniff/DENY.
    [Fact]
    public async Task StaticFile_TextAsset_ServedDirectlyWithSecurityHeaders()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();
        var expected = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "hosting-smoke.txt"));

        var response = await client.GetAsync("/assets/hosting-smoke.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
        Assert.Equal(SecurityHeaders.Nosniff, response.Headers.GetValues(SecurityHeaders.XContentTypeOptions).Single());
        Assert.Equal(SecurityHeaders.Deny, response.Headers.GetValues(SecurityHeaders.XFrameOptions).Single());
    }

    [Fact]
    public async Task StaticFile_Favicon_ServedDirectlyWithSecurityHeaders()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/favicon.ico");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/x-icon", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SecurityHeaders.Nosniff, response.Headers.GetValues(SecurityHeaders.XContentTypeOptions).Single());
        Assert.Equal(SecurityHeaders.Deny, response.Headers.GetValues(SecurityHeaders.XFrameOptions).Single());
    }

    // FR-002 «Нет index.html»: каталог wwwroot отсутствует — несовпавшие
    // SPA-маршруты отвечают 404-конвертом; /health продолжает обслуживаться.
    [Fact]
    public async Task SpaFallback_AbsentIndexHtml_ReturnsNotFoundEnvelope_HealthStillServed()
    {
        var absentWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot-absent-gate");
        using var factory = new TestWebAppFactory(settings: Settings((WebHostDefaults.WebRootKey, absentWebRoot)));
        using var client = factory.CreateClient();

        var worksResponse = await client.GetAsync("/works");
        var rootResponse = await client.GetAsync("/");
        var healthResponse = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, worksResponse.StatusCode);
        Assert.Equal("application/json", worksResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await worksResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, rootResponse.StatusCode);
        Assert.Equal(NotFoundEnvelope, await rootResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal(HealthBody, await healthResponse.Content.ReadAsStringAsync());
    }

    // /api не перехватывается fallback: несопоставленный маршрут — 404-конверт,
    // сопоставленный защищённый эндпойнт без аутентификации — 401-конверт;
    // в обоих случаях тело — JSON, а не index.html.
    [Fact]
    public async Task SpaFallback_ApiRoutes_NotInterceptedByFallback()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var unmapped = await client.GetAsync("/api/v1/nothing");

        Assert.Equal(HttpStatusCode.NotFound, unmapped.StatusCode);
        Assert.Equal("application/json", unmapped.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await unmapped.Content.ReadAsStringAsync());

        var labs = await client.GetAsync("/api/v1/labs");
        var labsBody = await labs.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, labs.StatusCode);
        Assert.Equal("application/json", labs.Content.Headers.ContentType?.MediaType);
        Assert.Equal(UnauthorizedEnvelope, labsBody);
        Assert.DoesNotContain("<html", labsBody, StringComparison.OrdinalIgnoreCase);
    }

    // FR-001: в Development /swagger* обслуживается Swagger UI (не fallback).
    [Fact]
    public async Task Swagger_Development_ServedBySwaggerNotFallback()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();
        var indexHtml = await ReadIndexHtmlAsync();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(indexHtml, await response.Content.ReadAsStringAsync());
    }

    // FR-002 (ISS-008): вне Development /swagger и /swagger/index.html —
    // 404-конверт, никогда не содержимое index.html.
    [Fact]
    public async Task Swagger_OutsideDevelopment_NotServedByFallback()
    {
        using var factory = new TestWebAppFactory(Environments.Production);
        using var client = factory.CreateClient();
        var indexHtml = await ReadIndexHtmlAsync();

        foreach (var path in new[] { "/swagger", "/swagger/index.html" })
        {
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
            Assert.NotEqual(indexHtml, await response.Content.ReadAsStringAsync());
        }
    }

    // ------------------------------------------------------------------
    // FR-001: /health — анонимно, точное тело, время ответа < 100 мс
    // (замер Stopwatch; первый запрос — прогрев построения конвейера).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Health_AnonymousExactJson_RespondsWithinHundredMilliseconds()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.GetAsync("/health");
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HealthBody, await response.Content.ReadAsStringAsync());
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(100),
                $"Попытка {attempt}: ответ /health занял {stopwatch.Elapsed.TotalMilliseconds:F1} мс — FR-001 требует < 100 мс.");
        }
    }

    // ------------------------------------------------------------------
    // Конверт FR-023: 400-форма / Route-404 / 500-ветка.
    // ------------------------------------------------------------------

    // AC «Форма 400»: полевая валидация — 400 с errors-картой: на каждое
    // нарушенное поле — непустой массив строк. Транспорт — анонимная пакетная
    // валидация POST /auth/register с телом «{}» (доменные сценарии вне T-112).
    [Fact]
    public async Task FieldValidation400_CarriesErrorsMap()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/auth/register",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(ErrorTexts.InvalidData, root.GetProperty("message").GetString());

        var errors = root.GetProperty("errors");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.Equal(RegisterErrorFields.Length, errors.EnumerateObject().Count());
        Assert.All(RegisterErrorFields, field =>
        {
            var fieldErrors = errors.GetProperty(field);
            Assert.Equal(JsonValueKind.Array, fieldErrors.ValueKind);
            Assert.True(fieldErrors.GetArrayLength() > 0, $"Поле '{field}' имеет пустой список ошибок.");
            Assert.All(fieldErrors.EnumerateArray(), item =>
            {
                Assert.Equal(JsonValueKind.String, item.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(item.GetString()));
            });
        });
    }

    // IF-001: errors присутствует ТОЛЬКО у 400 полевой валидации — фреймворковый
    // 400 (синтаксически некорректный JSON) отвечает конвертом без errors.
    [Fact]
    public async Task Framework400_MalformedJson_NoErrorsMap()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/host-smoke/bind",
            new StringContent("{not-json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(InvalidDataEnvelope, await response.Content.ReadAsStringAsync());
    }

    // AC «Неизвестный маршрут API» (дословный путь требования): 404-конверт.
    [Fact]
    public async Task Route404_UnknownApiRoute_ReturnsNotFoundEnvelope()
    {
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/nothing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundEnvelope, await response.Content.ReadAsStringAsync());
    }

    // AC «500 без деталей»: сервис заменён реализацией, бросающей исключение
    // (DI-замена ISecurityEventLogger через WithWebHostBuilder); исключение
    // выбрасывает подставленный сервис, контроллер лишь вызывает его. Ответ —
    // 500-конверт без имён типов и stack trace.
    [Fact]
    public async Task InjectedServiceFailure500_EnvelopeWithoutDetails()
    {
        using var baseFactory = new TestWebAppFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.RemoveAll<ISecurityEventLogger>()
                    .AddSingleton<ISecurityEventLogger>(new ThrowingSecurityEventLogger())));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/host-gate/injected-failure");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(InternalErrorEnvelope, body);
        Assert.DoesNotContain(HostingGateSmokeController.InjectedFailureDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    /// <summary>Бросающая реализация IF-016 — инжектированный сбой сервиса (FR-023).</summary>
    private sealed class ThrowingSecurityEventLogger : ISecurityEventLogger
    {
        public void LogRefreshTokenRevoked(string reason) =>
            throw new InvalidOperationException(HostingGateSmokeController.InjectedFailureDetail);

        public void LogPasswordChanged() =>
            throw new InvalidOperationException(HostingGateSmokeController.InjectedFailureDetail);

        public void LogPasswordReset() =>
            throw new InvalidOperationException(HostingGateSmokeController.InjectedFailureDetail);
    }

    // ------------------------------------------------------------------
    // Аудит проекта (FR-001/FR-024): net8.0, 0 вхождений
    // EntityFrameworkCore/Npgsql в файлах приложения.
    // ------------------------------------------------------------------

    [Fact]
    public void ProjectAudit_TargetFramework_IsNet8()
    {
        var document = XDocument.Load(LabsAppCsprojPath());

        var targetFramework = document
            .Descendants("TargetFramework")
            .Select(element => element.Value)
            .Single();

        Assert.Equal("net8.0", targetFramework);
    }

    // Пофайловый поиск маркера по всем файлам приложения (LabsApp/**, без
    // bin/obj); csproj входит в скан — запрет покрывает и PackageReference.
    // Сборка LabsApp.Tests в скан не входит: её аудит-тест цитирует маркеры как
    // запрещённые литералы.
    [Theory]
    [InlineData("EntityFrameworkCore")]
    [InlineData("Npgsql")]
    public void ProjectAudit_SourceTree_ContainsNoForbiddenDependency(string marker)
    {
        var files = AppProjectSourceFiles();

        Assert.True(
            files.Count > 10,
            $"Скан проекта подозрительно мал ({files.Count} файлов) — проверь LabsAppDirectory().");

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.True(
                !source.Contains(marker, StringComparison.OrdinalIgnoreCase),
                $"{Path.GetRelativePath(LabsAppDirectory(), file)} содержит '{marker}' — запрещённая зависимость (FR-001/FR-024).");
        }
    }

    // ------------------------------------------------------------------
    // Помощники.
    // ------------------------------------------------------------------

    private static Task<string> ReadIndexHtmlAsync() =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));

    private static string LabsAppDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "LabsApp"));

    private static string LabsAppCsprojPath() => Path.Combine(LabsAppDirectory(), "LabsApp.csproj");

    private static IReadOnlyList<string> AppProjectSourceFiles() =>
        Directory
            .EnumerateFiles(LabsAppDirectory(), "*", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

    private static IReadOnlyDictionary<string, string?> Settings(params (string Key, string? Value)[] items) =>
        items.ToDictionary(item => item.Key, item => item.Value);
}
