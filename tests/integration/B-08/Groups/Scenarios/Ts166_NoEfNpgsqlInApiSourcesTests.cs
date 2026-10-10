using System.Xml.Linq;

namespace LabsApp.IntegrationTests.B08.Groups.Scenarios;

/// <summary>
/// TS-166 «Репозитории: отсутствие EF/Npgsql в исходниках продуктового
/// проекта» (scope, FR-024, P0) — в редакции арбитража a-054/CR-001, опция (а).
///
/// given: зона проверки — продуктовый проект src/api/LabsApp фактического
///        дерева: все файлы *.cs и *.csproj рекурсивно, кроме подкаталогов
///        bin/ и obj/ (сборочные артефакты). Осознанно ВНЕ зоны: тестовый
///        проект src/api/LabsApp.Tests с антирегрессионным юнит-гвардом
///        HostingProjectFileTests.cs (его собственные литералы 'EntityFramework'/
///        'Npgsql' — необходимая часть его проверки, зону которого этот кейс
///        не дублирует), документация src/api/README.md (упоминание
///        EF Core/PostgreSQL ОБЯЗАТЕЛЬНО по FR-028(5) и утечкой не является),
///        каталоги артефактов прогонов TestResults.
/// when:  рекурсивный поиск подстрок 'EntityFrameworkCore' и 'Npgsql'
///        (С УЧЁТОМ РЕГИСТРА, ordinal) по содержимому *.cs и *.csproj зоны;
///        наряду с ним — разбор элементов PackageReference файла
///        src/api/LabsApp/LabsApp.csproj.
/// then:  0 вхождений 'EntityFrameworkCore' и 'Npgsql' в *.cs и *.csproj
///        зоны; среди PackageReference файла LabsApp.csproj отсутствуют
///        Microsoft.EntityFrameworkCore* и Npgsql* (FR-024 AC «Нет утечки EF»
///        в редакции арбитража: критерий «0 вхождений» действует в границах
///        продуктового проекта — именно его исходники и файлы проекта обязаны
///        не содержать EF/Npgsql).
/// </summary>
public sealed class Ts166_NoEfNpgsqlInApiSourcesTests
{
    /// <summary>Запрещённые подстроки содержимого (поиск с учётом регистра).</summary>
    private static readonly string[] ForbiddenTokens = ["EntityFrameworkCore", "Npgsql"];

    /// <summary>Запрещённые префиксы имён пакетов PackageReference (EF/Npgsql).</summary>
    private static readonly string[] ForbiddenPackagePrefixes = ["Microsoft.EntityFrameworkCore", "Npgsql"];

    [Fact]
    public async Task ProductProjectSourcesContainNeitherEntityFrameworkCoreNorNpgsql()
    {
        // given: зона — продуктовый проект src/api/LabsApp (*.cs и *.csproj,
        // рекурсивно, без bin/obj); файл проекта src/api/LabsApp/LabsApp.csproj.
        var productProjectDir = FindProductProjectDirectory();
        var projectFile = Path.Combine(productProjectDir, "LabsApp.csproj");
        Assert.True(
            File.Exists(projectFile),
            $"Файл проекта {projectFile} не найден — зона TS-166 неполна.");

        // when: рекурсивный поиск 'EntityFrameworkCore' и 'Npgsql' (с учётом
        // регистра) по содержимому файлов зоны (*.cs и *.csproj, без bin/obj).
        var contentViolations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(productProjectDir, "*", SearchOption.AllDirectories))
        {
            if (!IsZoneSourceFile(file))
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file);
            }
            catch (Exception)
            {
                // Нечитаемые строкой файлы зоны (*.cs/*.csproj — всегда текст)
                // невозможны; на всякий случай файл не молча засчитывается
                // чистым, а помечается нарушением проверки.
                contentViolations.Add($"{Path.GetRelativePath(productProjectDir, file)}: файл не прочитан");
                continue;
            }

            foreach (var token in ForbiddenTokens)
            {
                if (content.Contains(token, StringComparison.Ordinal))
                {
                    contentViolations.Add($"{Path.GetRelativePath(productProjectDir, file)}: «{token}»");
                }
            }
        }

        // when: наряду с поиском — разбор PackageReference файла LabsApp.csproj
        // (XElement-разбор: элементы внутри XML-комментариев комментарием и
        // остаются, в PackageReference не превращаются).
        var packageViolations = new List<string>();
        var projectXml = XDocument.Load(projectFile);
        foreach (var reference in projectXml.Descendants("PackageReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            if (include is null)
            {
                continue;
            }

            foreach (var prefix in ForbiddenPackagePrefixes)
            {
                if (include.StartsWith(prefix, StringComparison.Ordinal))
                {
                    packageViolations.Add(include);
                }
            }
        }

        // then: 0 вхождений 'EntityFrameworkCore'/'Npgsql' в *.cs и *.csproj зоны.
        Assert.True(
            contentViolations.Count == 0,
            "FR-024 (AC «Нет утечки EF», арбитраж a-054) нарушен: в продуктовом проекте " +
            "src/api/LabsApp найдены строки EF/Npgsql:\n" + string.Join("\n", contentViolations));

        // then: среди PackageReference LabsApp.csproj нет Microsoft.EntityFrameworkCore* и Npgsql*.
        Assert.True(
            packageViolations.Count == 0,
            "FR-024 (AC «Нет утечки EF», арбитраж a-054) нарушен: в PackageReference файла " +
            "src/api/LabsApp/LabsApp.csproj найдены запрещённые пакеты: " +
            string.Join(", ", packageViolations));
    }

    /// <summary>
    /// Файл зоны: расширение *.cs или *.csproj и ни один сегмент пути — не
    /// bin/ и не obj/ (сборочные артефакты исключены кейсом).
    /// </summary>
    private static bool IsZoneSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(segment => segment is "bin" or "obj");
    }

    /// <summary>
    /// Каталог src/api/LabsApp репозитория — вверх по дереву от каталога
    /// сборки тестовой зоны (детерминированно: зона живёт внутри репозитория).
    /// </summary>
    private static string FindProductProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "api", "LabsApp");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new Xunit.Sdk.XunitException(
            $"Каталог src/api/LabsApp не найден вверх по дереву от {AppContext.BaseDirectory} (TS-166).");
    }
}
