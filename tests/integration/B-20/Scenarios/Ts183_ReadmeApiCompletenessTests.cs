using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// TS-183 «README src/api: полнота таблицы переменных и инструкций»
/// (FR-028 AC «Полнота README», спецификация v2.2).
/// given: README в src/api.
/// when: статическая проверка содержания — grep переменных и разделов.
/// then: перечислены Auth__*, Labs__MaxSemester, Seed__* и Seed__DemoData
/// с умолчаниями (FR-028 п.1); в Production guard отвергает
/// Seed__TeacherPassword по умолчанию (подсказка дефолта только для
/// Development); команды dotnet/ng описаны; порядок сборки клиента в wwwroot
/// и поведение SPA fallback описаны; CORS отмечен как не настраиваемый
/// никогда (FR-001). Переменные RateLimits__*/Cors__*/ForwardedHeaders__*
/// и обработка X-Forwarded-For в v2.2 НЕ существуют (ADR-005/ADR-006:
/// конфигурация удалена, XFF вне области) — их документирование не
/// требуется.
///
/// Реализация: детерминированный статический греп (без сети и процессов). Регистр и
/// пробелы (в т.ч. разметка markdown-таблиц и переносы строк) нормализуются; «разделом»
/// считается блок текста между пустыми строками.
/// </summary>
public sealed class Ts183_ReadmeApiCompletenessTests
{
    /// <summary>
    /// Полный перечень конфигурационных переменных приложения v2.2 (FR-028 п.1):
    /// Auth__JwtKey/Auth__AccessTtlMinutes/Auth__RefreshTtlDays/Auth__Pbkdf2Iterations,
    /// Labs__MaxSemester, Seed__TeacherLogin/Seed__TeacherPassword/Seed__DemoData
    /// (те же константы, что читает приложение: Hosting/Configuration/*Options).
    /// RateLimits__*/Cors__*/ForwardedHeaders__* в v2.2 удалены (ADR-005/ADR-006).
    /// </summary>
    private static readonly string[] RequiredVariables =
    [
        "Auth__JwtKey",
        "Auth__AccessTtlMinutes",
        "Auth__RefreshTtlDays",
        "Auth__Pbkdf2Iterations",
        "Labs__MaxSemester",
        "Seed__TeacherLogin",
        "Seed__TeacherPassword",
        "Seed__DemoData",
    ];

    /// <summary>
    /// TS-183 / then: «Перечислены Auth__*, Labs__MaxSemester, Seed__* …
    /// и Seed__DemoData» (перечень v2.2 — FR-028 п.1).
    /// </summary>
    [Fact]
    public async Task VariablesTable_ListsAllConfigurationVariables()
    {
        var (fullText, _) = await ReadReadmeAsync();

        foreach (var variable in RequiredVariables)
        {
            AssertContains(fullText, variable.ToLowerInvariant(), $"переменная {variable} не перечислена");
        }
    }

    /// <summary>
    /// TS-183 / then: переменные перечислены «с умолчаниями» (FR-028 п.1);
    /// Seed__DemoData — «с умолчаниями» окружений (Development — true,
    /// вне Development — всегда false, FR-007).
    /// </summary>
    [Fact]
    public async Task VariablesTable_ListsDefaults()
    {
        var (_, blocks) = await ReadReadmeAsync();

        foreach (var variable in RequiredVariables)
        {
            Assert.True(
                AnyBlock(blocks, variable.ToLowerInvariant(), "умолчан"),
                $"README src/api: у переменной {variable} не указано умолчание "
                + "(ожидался раздел, где рядом с переменной есть «умолчание/по умолчанию»).");
        }

        // Seed__DemoData «с умолчаниями»: умолчания обоих окружений
        // (Development — true, Production — всегда false, FR-007).
        Assert.True(
            AnyBlock(blocks, "seed__demodata", "true") && AnyBlock(blocks, "seed__demodata", "false"),
            "README src/api: для Seed__DemoData не указаны умолчания окружений (true/false).");
    }

    /// <summary>
    /// TS-183 / then (v2.2 — FR-028 п.3, SEC-001): Production-guard задокументирован —
    /// в Production старт блокируется при незаданном или дефолтном
    /// Seed__TeacherPassword; требуется собственный пароль; dev-умолчание
    /// teacher123! помечено как «только Development» и не подсказывается
    /// для прод-развёртывания.
    /// </summary>
    [Fact]
    public async Task Secrets_MarkedAsRejectedInProduction()
    {
        var (fullText, blocks) = await ReadReadmeAsync();

        // Dev-умолчание пароля сида помечено «только Development».
        Assert.True(
            AnyBlock(blocks, "seed__teacherpassword", "development"),
            "README src/api: dev-умолчание Seed__TeacherPassword не помечено "
            + "«только Development» (FR-028 п.3).");

        // Guard Production: раздел с блокировкой старта и требованием
        // собственного пароля; без подсказки dev-дефолта.
        var guardBlocks = blocks
            .Where(block => block.Contains("production", StringComparison.Ordinal)
                && block.Contains("блокиру", StringComparison.Ordinal))
            .ToList();
        Assert.True(
            guardBlocks.Any(block => block.Contains("собственн", StringComparison.Ordinal)),
            "README src/api: не описан Production-guard Seed__TeacherPassword "
            + "(старт блокируется, требуется собственный пароль — FR-028 п.3, SEC-001).");
        Assert.True(
            guardBlocks.Any(block => !block.Contains("teacher123!", StringComparison.Ordinal)),
            "README src/api: Production-guard подсказывает dev-дефолт teacher123! "
            + "(FR-028 п.3: README не должен подсказывать дефолт для прод-развёртывания).");

        // Общий текст README упоминает Production-guard.
        AssertContains(fullText, "production", "упоминание Production-guard не найдено");
    }

    /// <summary>
    /// TS-183 / then (v2.2 — FR-001): граница инфраструктуры задокументирована —
    /// CORS не настраивается никогда; обработка X-Forwarded-For вне области
    /// (ADR-006), документирование не требуется.
    /// </summary>
    [Fact]
    public async Task CorsNeverConfigured_Documented()
    {
        var (fullText, _) = await ReadReadmeAsync();

        AssertContains(
            fullText,
            "cors не настраивается",
            "требование FR-001 «CORS не настраивается никогда» не отражено в README");
    }

    /// <summary>
    /// TS-183 / then: «команды dotnet/ng» (запуск бэкенда, сборка/тесты —
    /// dotnet run/build/test; сборка/тесты клиента — ng build/test).
    /// </summary>
    [Fact]
    public async Task DotnetAndNgCommands_Documented()
    {
        var (fullText, _) = await ReadReadmeAsync();

        foreach (var command in new[] { "dotnet run", "dotnet build", "dotnet test", "ng build", "ng test" })
        {
            AssertContains(fullText, command, $"команда {command} не описана");
        }
    }

    /// <summary>
    /// TS-183 / then: «порядок сборки клиента в wwwroot».
    /// </summary>
    [Fact]
    public async Task ClientBuildToWwwroot_OrderDescribed()
    {
        var (_, blocks) = await ReadReadmeAsync();

        Assert.True(
            AnyBlock(blocks, "wwwroot", "ng build"),
            "README src/api: не описан порядок сборки клиента в wwwroot "
            + "(ожидался раздел, где wwwroot упомянут вместе с ng build).");
    }

    /// <summary>
    /// TS-183 / then: «поведение SPA fallback» (не-/api маршруты отдают index.html).
    /// </summary>
    [Fact]
    public async Task SpaFallback_BehaviorDescribed()
    {
        var (fullText, _) = await ReadReadmeAsync();

        AssertContains(fullText, "spa fallback", "раздел о поведении SPA fallback не найден");
        AssertContains(fullText, "index.html", "поведение SPA fallback (отдача index.html) не описано");
    }

    /// <summary>Читает src/api/README.md: нормализованный полный текст и блоки-разделы.</summary>
    private static async Task<(string FullText, string[] Blocks)> ReadReadmeAsync()
    {
        var path = RepoPaths.SrcApiReadmePath;
        Assert.True(File.Exists(path), $"Не найден README в src/api: {path}.");

        var text = await File.ReadAllTextAsync(path);

        var fullText = Normalize(text);
        var blocks = Regex
            .Split(text, @"\r?\n\s*\r?\n")
            .Select(Normalize)
            .Where(block => block.Length > 0)
            .ToArray();

        return (fullText, blocks);
    }

    /// <summary>Нижний регистр; любые пробельные символы схлопываются в один пробел.</summary>
    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").ToLowerInvariant().Trim();

    private static void AssertContains(string haystack, string needle, string what) =>
        Assert.True(
            haystack.Contains(needle, StringComparison.Ordinal),
            $"README src/api: {what} — не найдено «{needle}».");

    /// <summary>Существует раздел-блок, содержащий все указанные подстроки.</summary>
    private static bool AnyBlock(string[] blocks, params string[] needles) =>
        blocks.Any(block => needles.All(needle => block.Contains(needle, StringComparison.Ordinal)));
}
