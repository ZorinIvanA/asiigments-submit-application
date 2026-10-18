using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-147 (FR-028, P1): полнота README src/api — разделы (1)–(6).
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
///        auth_kdf_operations_total >100/с на 5 минут; указание о подключении
///        EF Core/PostgreSQL в следующей итерации заменой in-memory реализаций;
///        заметка о dev-почте в категории 'EmailDev' с маркером [DEV-EMAIL]
///        (FR-028 AC «Полнота README»).
///
/// Детерминизм: тест читает только файл src/api/README.md — внешние системы,
/// время и случайность не участвуют; код реализации не изменяется.
/// </summary>
public sealed class Ts147_ReadmeSectionsTests
{
    /// <summary>
    /// TS-147 / then «присутствуют разделы (1)–(6)»: для каждого раздела FR-028
    /// в README src/api присутствуют его обязательные сведения.
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
        // given: реализация бэкенда завершена; файл README в src/api.
        var readme = ReadApiReadme();

        switch (section)
        {
            case 1:
                {
                    // (1) Запуск и порт: команда запуска и http://localhost:5080.
                    Assert.Contains("dotnet run", readme, StringComparison.Ordinal);
                    Assert.Contains("http://localhost:5080", readme, StringComparison.Ordinal);

                    // (1) Переменные окружения со значениями по умолчанию
                    // (каждая — строкой, в колонке «По умолчанию»).
                    var jwtKey = LineContaining(readme, "`Auth__JwtKey`");
                    Assert.True(
                        jwtKey.Contains("не задана", StringComparison.Ordinal)
                        || jwtKey.Contains("умолчани", StringComparison.Ordinal),
                        "FR-028 (1): для Auth__JwtKey не описано значение по умолчанию "
                        + $"(ожидалось «не задана»/описание умолчания), строка: {jwtKey}");
                    Assert.Contains("`15`", LineContaining(readme, "`Auth__AccessTtlMinutes`"), StringComparison.Ordinal);
                    Assert.Contains("`7`", LineContaining(readme, "`Auth__RefreshTtlDays`"), StringComparison.Ordinal);
                    Assert.Contains("`210000`", LineContaining(readme, "`Auth__Pbkdf2Iterations`"), StringComparison.Ordinal);
                    Assert.Contains("`10`", LineContaining(readme, "`Labs__MaxSemester`"), StringComparison.Ordinal);
                    Assert.Contains("`teacher`", LineContaining(readme, "`Seed__TeacherLogin`"), StringComparison.Ordinal);
                    Assert.Contains("`teacher123!`", LineContaining(readme, "`Seed__TeacherPassword`"), StringComparison.Ordinal);
                    Assert.Contains(
                        "умолчани",
                        LineContaining(readme, "`Seed__DemoData`"),
                        StringComparison.OrdinalIgnoreCase);
                    break;
                }

            case 2:
                {
                    // (2) Схема dev-прокси: ng serve + proxy.conf.json с путями
                    // /api и /health; сборка клиента в wwwroot для прод-режима.
                    Assert.Contains("ng serve", readme, StringComparison.Ordinal);
                    var proxyLine = LineContaining(readme, "proxy.conf.json");
                    Assert.Contains("`/api`", proxyLine, StringComparison.Ordinal);
                    Assert.Contains("`/health`", proxyLine, StringComparison.Ordinal);
                    Assert.Contains("wwwroot", readme, StringComparison.Ordinal);
                    break;
                }

            case 3:
                {
                    // (3) Сид-учётки (teacher / studentNN) и ЯВНОЕ предупреждение
                    // SEC-001: в Production старт блокируется при незаданном или
                    // дефолтном Seed__TeacherPassword; дефолт для прода не
                    // подсказывается.
                    Assert.Contains("`teacher`", readme, StringComparison.Ordinal);
                    Assert.Contains("studentNN", readme, StringComparison.Ordinal);

                    var warning = FragmentUntilNextHeading(readme, "SEC-001");
                    Assert.Contains("Production", warning, StringComparison.Ordinal);
                    Assert.Contains("блокируется", warning, StringComparison.Ordinal);
                    Assert.Contains("Seed__TeacherPassword", warning, StringComparison.Ordinal);
                    Assert.Contains("не задана", warning, StringComparison.Ordinal);
                    Assert.Contains("умолчанию", warning, StringComparison.Ordinal);
                    Assert.Contains("не документируется", warning, StringComparison.Ordinal);
                    break;
                }

            case 4:
                {
                    // (4) Оперативная заметка о KDF-поверхности: амплитуда входа
                    // 50000 KDF/мин (≈833/с) и порог алертинга
                    // auth_kdf_operations_total >100/с в течение 5 минут.
                    Assert.Contains("50000 KDF/мин", readme, StringComparison.Ordinal);
                    Assert.Contains("≈833/с", readme, StringComparison.Ordinal);
                    Assert.Contains("auth_kdf_operations_total", readme, StringComparison.Ordinal);
                    Assert.Contains("100/с в течение 5 минут", readme, StringComparison.Ordinal);
                    break;
                }

            case 5:
                {
                    // (5) EF Core/PostgreSQL подключаются в следующей итерации
                    // заменой in-memory реализаций интерфейсов репозиториев.
                    Assert.Contains("EF Core", readme, StringComparison.Ordinal);
                    Assert.Contains("PostgreSQL", readme, StringComparison.Ordinal);
                    Assert.Contains("заменой in-memory реализаций", readme, StringComparison.Ordinal);
                    break;
                }

            case 6:
                {
                    // (6) Заметка о dev-почте: категория 'EmailDev' с маркером
                    // [DEV-EMAIL].
                    Assert.Contains("EmailDev", readme, StringComparison.Ordinal);
                    var devEmailLine = LineContaining(readme, "[DEV-EMAIL]");
                    Assert.Contains("EmailDev", devEmailLine, StringComparison.Ordinal);
                    break;
                }

            default:
                Assert.Fail($"Раздел {section} FR-028 не входит в матрицу (1)–(6).");
                break;
        }
    }

    /// <summary>Прочитать README src/api (с проверкой предусловия given).</summary>
    private static string ReadApiReadme()
    {
        var path = Path.Combine(B17RepoPaths.RepositoryRoot, "src", "api", "README.md");
        Assert.True(
            File.Exists(path),
            $"Предусловие кейса TS-147: не найден файл README src/api по пути {path}.");
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
            $"TS-147/FR-028: в README src/api не найдена строка, содержащая «{needle}».");
        return line;
    }

    /// <summary>
    /// Фрагмент текста от первого вхождения маркера до следующего заголовка
    /// «## » (инспекция предупреждения SEC-001 внутри его раздела).
    /// </summary>
    private static string FragmentUntilNextHeading(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            $"TS-147/FR-028: в README src/api не найден маркер «{marker}».");
        var nextHeading = text.IndexOf("\n## ", start, StringComparison.Ordinal);
        return nextHeading < 0 ? text[start..] : text[start..nextHeading];
    }
}
