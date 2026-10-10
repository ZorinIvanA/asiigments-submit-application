using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Тестовый хост «стендов» кейса TS-195 (scope, FR-002): вариант стенда задаёт
/// содержимое wwwroot — <see cref="WwwrootMode.NoIndexHtml"/> (каталог wwwroot
/// отсутствует: content root переносится в пустой временный каталог, там нет и
/// appsettings.json — конфигурация складывается из умолчаний Development) и
/// <see cref="WwwrootMode.IndexHtmlPresent"/> (wwwroot/index.html с маркерным
/// содержимым). Механика — копия фабрик зоны B-13/B-01 (фабрики чужих зон
/// internal — BL-001 BUG-001): Development, фиксированный Auth:JwtKey,
/// Seed:TeacherPassword зафиксирован, демо-сид выключен ЯВНО. Временные каталоги
/// удаляются при освобождении фабрики.
/// </summary>
public sealed class B13ScopeWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Вариант стенда по given кейса TS-195.</summary>
    public enum WwwrootMode
    {
        /// <summary>Стенд (а): каталог wwwroot пуст/отсутствует — index.html нет.</summary>
        NoIndexHtml,

        /// <summary>Стенд (б): wwwroot/index.html существует (маркерное содержимое).</summary>
        IndexHtmlPresent,
    }

    public const string TestJwtKey = "b13-scope-jwt-signing-key-0123456789abcdef-0123456789abcdef";

    /// <summary>Маркер в теле index.html стенда (б) — проверка «тело = index.html».</summary>
    public const string IndexHtmlMarker = "b13-ts195-marker-index-html";

    private const string IndexHtmlContent =
        "<!doctype html><html><head><title>b13-ts195</title></head><body>" + IndexHtmlMarker + "</body></html>";

    private readonly WwwrootMode _mode;
    private readonly string _contentRoot;

    public B13ScopeWebAppFactory(WwwrootMode mode)
    {
        _mode = mode;
        _contentRoot = Directory.CreateTempSubdirectory("b13-scope-").FullName;
        if (mode == WwwrootMode.IndexHtmlPresent)
        {
            Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot"));
            File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "index.html"), IndexHtmlContent);
        }
    }

    /// <summary>Байты маркерного index.html стенда (ожидаемое тело SPA-fallback).</summary>
    public byte[] IndexHtmlBytes =>
        _mode == WwwrootMode.IndexHtmlPresent
            ? File.ReadAllBytes(Path.Combine(_contentRoot, "wwwroot", "index.html"))
            : throw new InvalidOperationException("Стенд без index.html не имеет маркерного файла.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, _contentRoot);

        // Короткоживущие тестовые хосты не перезагружают конфигурацию (как в зоне B-01).
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), SeedOptions.DefaultTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Временный каталог — расходный материал: невозможность удалить его
                // не меняет результат теста и не должна маскировать исключение сценария.
            }
        }

        base.Dispose(disposing);
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
