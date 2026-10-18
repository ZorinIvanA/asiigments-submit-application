using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-177 «README src/api: разделы (1)–(6)» (FR-028, P1).
///
/// given: README src/api — файл src/api/README.md.
/// when:  чтение README.
/// then:  присутствуют: (1) команда запуска, порт http://localhost:5080,
///        переменные Auth__JwtKey, Auth__AccessTtlMinutes, Auth__RefreshTtlDays,
///        Auth__Pbkdf2Iterations, Labs__MaxSemester, Seed__TeacherLogin/Password,
///        Seed__DemoData со значениями по умолчанию; (2) dev-схема ng serve +
///        proxy.conf.json и сборка клиента в wwwroot; (3) сид-учётки с
///        предупреждением SEC-001 (в Production старт блокируется при
///        незаданном/дефолтном Seed__TeacherPassword); (4) KDF-заметка
///        (амплитуда — как в TS-023: 50000 KDF/мин (≈833/с), порог
///        auth_kdf_operations_total >100/с в течение 5 минут); (5) указание на
///        замену in-memory реализаций на EF Core/PostgreSQL в следующей
///        итерации; (6) заметка о dev-почте ('EmailDev', маркер [DEV-EMAIL] —
///        единственное место появления кода) (AC FR-028 «Полнота README»).
///
/// Детерминизм: тест читает только файл src/api/README.md — сеть, внешние
/// системы, время и случайность не участвуют; код реализации не изменяется.
/// Механика — по образцу Ts147_ReadmeSectionsTests этой же зоны (чтение файла
/// от B17RepoPaths.RepositoryRoot, построчные инспекции таблиц).
/// </summary>
public sealed class Ts177_ReadmeSectionsTests
{
    /// <summary>
    /// TS-177 / then «разделы (1)–(6)»: для каждого раздела FR-028 в README
    /// src/api присутствуют его обязательные сведения.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Readme_ContainsFr028Section(int section)
    {
        // given: README src/api — файл src/api/README.md; when: чтение README.
        var readme = ReadApiReadme();

        switch (section)
        {
            case 1:
                {
                    // (1) Команда запуска и порт по умолчанию http://localhost:5080.
                    Assert.Contains("dotnet run", readme, StringComparison.Ordinal);
                    Assert.Contains("http://localhost:5080", readme, StringComparison.Ordinal);

                    // (1) Переменные окружения — каждая перечислена со значением
                    // по умолчанию (значение указано в строке переменной).
                    Assert.Contains(
                        "не задана",
                        LineContaining(readme, "`Auth__JwtKey`"),
                        StringComparison.Ordinal);
                    Assert.Contains("`15`", LineContaining(readme, "`Auth__AccessTtlMinutes`"), StringComparison.Ordinal);
                    Assert.Contains("`7`", LineContaining(readme, "`Auth__RefreshTtlDays`"), StringComparison.Ordinal);
                    Assert.Contains("`210000`", LineContaining(readme, "`Auth__Pbkdf2Iterations`"), StringComparison.Ordinal);
                    Assert.Contains("`10`", LineContaining(readme, "`Labs__MaxSemester`"), StringComparison.Ordinal);
                    Assert.Contains("`teacher`", LineContaining(readme, "`Seed__TeacherLogin`"), StringComparison.Ordinal);
                    Assert.Contains("`teacher123!`", LineContaining(readme, "`Seed__TeacherPassword`"), StringComparison.Ordinal);
                    Assert.Contains(
                        "умолчани",
                        LineContaining(readme, "`Seed__DemoData`"),
                        StringComparison.Ordinal);
                    break;
                }

            case 2:
                {
                    // (2) Dev-схема: ng serve + proxy.conf.json (пути /api и
                    // /health проксируются на Kestrel); сборка клиента в wwwroot.
                    Assert.Contains("ng serve", readme, StringComparison.Ordinal);
                    var proxyLine = LineContaining(readme, "proxy.conf.json");
                    Assert.Contains("`/api`", proxyLine, StringComparison.Ordinal);
                    Assert.Contains("`/health`", proxyLine, StringComparison.Ordinal);
                    Assert.Contains("wwwroot", readme, StringComparison.Ordinal);
                    break;
                }

            case 3:
                {
                    // (3) Сид-учётки (teacher, studentNN) с предупреждением
                    // SEC-001: в Production старт блокируется при незаданном или
                    // дефолтном Seed__TeacherPassword.
                    Assert.Contains("`teacher`", readme, StringComparison.Ordinal);
                    Assert.Contains("`teacher123!`", readme, StringComparison.Ordinal);
                    Assert.Contains("studentNN", readme, StringComparison.Ordinal);
                    Assert.Contains("`student123!`", readme, StringComparison.Ordinal);

                    var warning = FragmentUntilNextHeading(readme, "SEC-001");
                    Assert.Contains("Production", warning, StringComparison.Ordinal);
                    Assert.Contains("блокируется", warning, StringComparison.Ordinal);
                    Assert.Contains("Seed__TeacherPassword", warning, StringComparison.Ordinal);
                    Assert.Contains("не задана", warning, StringComparison.Ordinal);
                    Assert.Contains("умолчанию", warning, StringComparison.Ordinal);
                    break;
                }

            case 4:
                {
                    // (4) KDF-заметка — амплитуда как в TS-023: вход ≈50000
                    // KDF/мин (≈833/с); порог алертинга auth_kdf_operations_total
                    // >100/с в течение 5 минут.
                    Assert.Contains("50000 KDF/мин", readme, StringComparison.Ordinal);
                    Assert.Contains("≈833/с", readme, StringComparison.Ordinal);
                    Assert.Contains("auth_kdf_operations_total", readme, StringComparison.Ordinal);
                    Assert.Contains("100/с в течение 5 минут", readme, StringComparison.Ordinal);
                    break;
                }

            case 5:
                {
                    // (5) Замена in-memory реализаций на EF Core/PostgreSQL в
                    // следующей итерации.
                    Assert.Contains("EF Core", readme, StringComparison.Ordinal);
                    Assert.Contains("PostgreSQL", readme, StringComparison.Ordinal);
                    Assert.Contains("заменой in-memory реализаций", readme, StringComparison.Ordinal);
                    Assert.Contains("следующ", readme, StringComparison.Ordinal);
                    break;
                }

            case 6:
                {
                    // (6) Заметка о dev-почте: категория 'EmailDev', маркер
                    // [DEV-EMAIL] — единственное место появления кода.
                    var section6 = FragmentUntilNextHeading(readme, "## 6.");
                    Assert.Contains("EmailDev", section6, StringComparison.Ordinal);
                    var devEmailLine = LineContaining(section6, "[DEV-EMAIL]");
                    Assert.Contains("EmailDev", devEmailLine, StringComparison.Ordinal);
                    Assert.Contains("единственное место", section6, StringComparison.Ordinal);
                    break;
                }

            default:
                Assert.Fail($"TS-177/FR-028: раздел {section} вне матрицы (1)–(6).");
                break;
        }
    }

    /// <summary>Прочитать README src/api (с проверкой предусловия given).</summary>
    private static string ReadApiReadme()
    {
        var path = Path.Combine(B17RepoPaths.RepositoryRoot, "src", "api", "README.md");
        Assert.True(
            File.Exists(path),
            $"Предусловие кейса TS-177: не найден файл README src/api по пути {path}.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Первая строка текста, содержащая указанную подстроку (инспекция
    /// табличных строк README со значениями по умолчанию).
    /// </summary>
    private static string LineContaining(string text, string needle)
    {
        var line = text.Split('\n')
            .FirstOrDefault(candidate => candidate.Contains(needle, StringComparison.Ordinal));
        Assert.True(
            line is not null,
            $"TS-177/FR-028: в README src/api не найдена строка, содержащая «{needle}».");
        return line;
    }

    /// <summary>
    /// Фрагмент текста от первого вхождения маркера до следующего заголовка
    /// «## » (инспекция предупреждения SEC-001 и раздела 6 внутри их разделов).
    /// </summary>
    private static string FragmentUntilNextHeading(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            $"TS-177/FR-028: в README src/api не найден маркер «{marker}».");
        var nextHeading = text.IndexOf("\n## ", start, StringComparison.Ordinal);
        return nextHeading < 0 ? text[start..] : text[start..nextHeading];
    }
}
