using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-147 «README src/api: полнота разделов (1)–(6)» (scope, FR-028, P1).
///
/// given: реализация бэкенда завершена; файл README в src/api.
/// when:  чтение README.
/// then:  присутствуют: запуск/порт http://localhost:5080 и переменные окружения
///        (Auth__JwtKey, Auth__AccessTtlMinutes, Auth__RefreshTtlDays,
///        Auth__Pbkdf2Iterations, Labs__MaxSemester, Seed__TeacherLogin/Password,
///        Seed__DemoData) со значениями по умолчанию; схема dev-прокси (ng serve +
///        proxy.conf.json /api,/health; сборка клиента в wwwroot); сид-учётки с
///        ЯВНЫМ предупреждением SEC-001 (в Production старт блокируется при
///        незаданном/дефолтном Seed__TeacherPassword; дефолт для прода не
///        подсказывается); амплитуда 50000 KDF/мин (≈833/с) и порог
///        auth_kdf_operations_total &gt;100/с на 5 минут; указание о подключении
///        EF Core/PostgreSQL в следующей итерации заменой in-memory реализаций;
///        заметка о dev-почте в категории 'EmailDev' с маркером [DEV-EMAIL]
///        (FR-028 AC «Полнота README»).
///
/// Реализация: детерминированный статический греп (без сети и процессов; образец
/// механики — Ts183 зоны B-20, чужие зоны недоступны для ссылок — изоляция зон,
/// BL-001). Регистр и пробельные символы (в т.ч. разметка markdown и переносы
/// строк) нормализуются; «разделом» считается блок текста между пустыми строками.
/// «Дефолт для прода не подсказывается» проверяется так: блок Production-guard
/// требует собственный пароль («собственн») и не называет dev-умолчание
/// teacher123! как значение (dev-умолчания сид-учёток — в разделе Development,
/// прод-значение не предлагается).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts147_ReadmeApiCompletenessTests
{
    /// <summary>
    /// Полный перечень конфигурационных переменных из then кейса TS-147: Auth__* —
    /// Auth__JwtKey/Auth__AccessTtlMinutes/Auth__RefreshTtlDays/Auth__Pbkdf2Iterations,
    /// Labs__MaxSemester, Seed__* — Seed__TeacherLogin/Seed__TeacherPassword
    /// (в кейсе «Seed__TeacherLogin/Password») и Seed__DemoData.
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
    /// Формулировки условия блокировки «незаданный или дефолтный»
    /// Seed__TeacherPassword (нормализованный нижний регистр).
    /// </summary>
    private static readonly string[] UnsetOrDefaultMarkers =
    [
        "незаданн",
        "не задан",
        "дефолтн",
        "умолчан",
        "пуст",
    ];

    /// <summary>
    /// TS-147 / then (1): «запуск/порт http://localhost:5080 и переменные окружения
    /// … со значениями по умолчанию» — команда запуска (dotnet run), порт
    /// по умолчанию http://localhost:5080 (ASPNETCORE_URLS) и каждая переменная
    /// с указанным умолчанием.
    /// </summary>
    [Fact]
    public void RunCommand_PortAndEnvVariablesWithDefaults_Documented()
    {
        var (fullText, blocks) = ReadReadme();

        // Требования и команда запуска бэкенда (FR-028 (1): dotnet run).
        AssertContains(fullText, "dotnet run", "команда запуска бэкенда (dotnet run) не описана");

        // Порт по умолчанию http://localhost:5080 — ASPNETCORE_URLS.
        AssertContains(fullText, "http://localhost:5080", "порт по умолчанию http://localhost:5080 не указан");
        AssertContains(fullText, "aspnetcore_urls", "связка порта с ASPNETCORE_URLS не описана");

        // Переменные окружения — каждая перечислена и со значением по умолчанию
        // (умолчание указано в том же разделе, что и переменная).
        foreach (var variable in RequiredVariables)
        {
            AssertContains(fullText, variable.ToLowerInvariant(), $"переменная окружения {variable} не перечислена");
            Assert.True(
                AnyBlock(blocks, variable.ToLowerInvariant(), "умолчан"),
                $"README src/api: у переменной {variable} не указано значение по умолчанию " +
                "(ожидался раздел, где рядом с переменной есть «умолчание/по умолчанию»).");
        }
    }

    /// <summary>
    /// TS-147 / then (2): «схема dev-прокси (ng serve + proxy.conf.json /api,/health;
    /// сборка клиента в wwwroot)».
    /// </summary>
    [Fact]
    public void DevProxyScheme_AndClientBuildToWwwroot_Documented()
    {
        var (_, blocks) = ReadReadme();

        // Dev-режим: ng serve с proxy.conf.json, проксирование /api и /health на Kestrel.
        Assert.True(
            AnyBlock(blocks, "ng serve", "proxy.conf.json", "/api", "/health"),
            "README src/api: не описана схема dev-прокси (ожидался раздел с ng serve, " +
            "proxy.conf.json и проксированием /api и /health на бэкенд).");

        // Прод-режим: сборка клиента в wwwroot.
        Assert.True(
            AnyBlock(blocks, "wwwroot", "сборк") || AnyBlock(blocks, "wwwroot", "ng build"),
            "README src/api: не описана сборка клиента в wwwroot для прод-режима.");
    }

    /// <summary>
    /// TS-147 / then (3): «сид-учётки с ЯВНЫМ предупреждением SEC-001 (в Production
    /// старт блокируется при незаданном/дефолтном Seed__TeacherPassword; дефолт для
    /// прода не подсказывается)». Идентификатор SEC-001 дословно не требуется —
    /// явность выражается содержанием предупреждения (как в Ts023 зоны B-07 для SEC-002).
    /// </summary>
    [Fact]
    public void SeedAccountsWithExplicitProductionGuard_Documented()
    {
        var (_, blocks) = ReadReadme();

        // Сид-учётки (FR-028 (3)): в Development teacher/teacher123!;
        // studentNN/student123! при демо-данных.
        Assert.True(
            AnyBlock(blocks, "teacher", "teacher123!"),
            "README src/api: не описана сид-учётка teacher (пароль teacher123!) для Development.");
        Assert.True(
            AnyBlock(blocks, "studentnn", "student123!"),
            "README src/api: не описаны сид-учётки studentNN (пароль student123!) при демо-данных.");

        // Явное предупреждение SEC-001: блок с Production-guard-ом Seed__TeacherPassword.
        var guardBlock = FindGuardBlock(blocks);

        // Условие блокировки: незаданный или дефолтный Seed__TeacherPassword.
        Assert.True(
            UnsetOrDefaultMarkers.Any(guardBlock.Contains),
            "README src/api: предупреждение Production-guard не называет условие блокировки — " +
            "незаданный или дефолтный Seed__TeacherPassword.");

        // Требуется собственный пароль (дефолт для прода не подсказывается).
        Assert.Contains("собственн", guardBlock);

        // Dev-умолчание teacher123! не предлагается в блоке guard-а как значение
        // для Production.
        Assert.True(
            !guardBlock.Contains("teacher123!", StringComparison.Ordinal),
            "README src/api: блок Production-guard подсказывает значение пароля (teacher123!) — " +
            "дефолт для прод-развёртывания не должен предлагаться.");
    }

    /// <summary>
    /// TS-147 / then (4): «амплитуда 50000 KDF/мин (≈833/с) и порог
    /// auth_kdf_operations_total &gt;100/с на 5 минут» (пороги совпадают с FR-004).
    /// </summary>
    [Fact]
    public void KdfAmplitude_AndAlertThreshold_Documented()
    {
        var (fullText, _) = ReadReadme();

        // Оценка амплитуды входа (нормативная величина FR-004): 50000 KDF/мин (≈833/с).
        AssertContains(fullText, "50000 kdf/мин", "оценка амплитуды 50000 KDF/мин не найдена");
        AssertContains(fullText, "≈833/с", "оценка ≈833/с не найдена");

        // Порог алертинга: auth_kdf_operations_total >100/с в течение 5 минут.
        AssertContains(fullText, "auth_kdf_operations_total", "метрика auth_kdf_operations_total не упомянута");
        Assert.True(
            fullText.Contains(">100/с", StringComparison.Ordinal) ||
            fullText.Contains("> 100/с", StringComparison.Ordinal),
            "README src/api: порог алертинга «auth_kdf_operations_total >100/с» не найден.");
        AssertContains(fullText, "5 минут", "окно порога алертинга (5 минут) не указано");
    }

    /// <summary>
    /// TS-147 / then (5): «указание о подключении EF Core/PostgreSQL в следующей
    /// итерации заменой in-memory реализаций».
    /// </summary>
    [Fact]
    public void EfCorePostgreSql_NextIterationNote_Documented()
    {
        var (_, blocks) = ReadReadme();

        Assert.True(
            AnyBlock(blocks, "ef core", "postgres", "in-memory", "следующ", "замен") ||
            AnyBlock(blocks, "ef core", "postgres", "in memory", "следующ", "замен"),
            "README src/api: не найдено указание, что EF Core/PostgreSQL подключаются " +
            "в следующей итерации заменой in-memory реализаций интерфейсов репозиториев.");
    }

    /// <summary>
    /// TS-147 / then (6): «заметка о dev-почте в категории 'EmailDev' с маркером
    /// [DEV-EMAIL]» — письма восстановления в Development видны только там.
    /// </summary>
    [Fact]
    public void DevEmail_EmailDevCategoryWithMarker_Documented()
    {
        var (_, blocks) = ReadReadme();

        Assert.True(
            AnyBlock(blocks, "emaildev", "[dev-email]", "восстанов"),
            "README src/api: не найдена заметка о dev-почте — категория лога 'EmailDev' " +
            "с маркером [DEV-EMAIL] для писем восстановления.");
    }

    /// <summary>
    /// Блок Production-guard-а Seed__TeacherPassword: раздел, где Production
    /// упомянут вместе с Seed__TeacherPassword и блокировкой старта.
    /// </summary>
    private static string FindGuardBlock(string[] blocks)
    {
        var guardBlock = blocks.FirstOrDefault(block =>
            block.Contains("production", StringComparison.Ordinal) &&
            block.Contains("seed__teacherpassword", StringComparison.Ordinal) &&
            block.Contains("блокир", StringComparison.Ordinal));

        Assert.True(
            guardBlock is not null,
            "README src/api: не найдено предупреждение о блокировке старта в Production " +
            "при незаданном/дефолтном Seed__TeacherPassword (SEC-001).");

        return guardBlock ?? string.Empty;
    }

    /// <summary>Читает src/api/README.md: нормализованный полный текст и блоки-разделы.</summary>
    private static (string FullText, string[] Blocks) ReadReadme()
    {
        // given: файл README в src/api.
        var path = B16RepoPaths.SrcApiReadmePath;
        Assert.True(File.Exists(path), $"Не найден README в src/api: {path}.");

        var text = File.ReadAllText(path);

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
