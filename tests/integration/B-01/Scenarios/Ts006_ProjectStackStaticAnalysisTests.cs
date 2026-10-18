using System.Xml.Linq;
using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-006 «Запрещённые зависимости отсутствуют: нет EF Core/Npgsql,
/// TFM net8.0» (FR-001, FR-024, P0).
/// given: файлы проекта src/api (LabsApp.csproj, LabsApp.Tests.csproj) и исходники
/// src/api.
/// when: анализ TargetFramework и PackageReference; текстовый поиск строк
/// 'EntityFrameworkCore' и 'Npgsql' (включая csproj).
/// then: TargetFramework=net8.0; ссылки Microsoft.EntityFrameworkCore* и Npgsql*
/// отсутствуют; поиск по исходникам даёт 0 вхождений.
///
/// Примечание к объёму поиска: «исходники» трактованы как производственные
/// исходники приложения (src/api/LabsApp) — собственный статик-гейт юнит-тестов
/// приложения (src/api/LabsApp.Tests/Hosting/HostingProjectFileTests.cs) содержит
/// литералы 'Npgsql'/'EntityFramework' как тестовые данные InlineData, поэтому
/// буквальный поиск по ВСЕМ .cs-файлам src/api давал бы ложное срабатывание на
/// сам гейт (расхождение объёма поиска оформлено scenario_change_request по
/// TS-006; в предыдущей нумерации зоны кейс значился как TS-004). Оба файла
/// проекта из given включены в поиск дословно.
/// </summary>
public sealed class Ts006_ProjectStackStaticAnalysisTests
{
    /// <summary>Префиксы запрещённых пакетов (FR-001: EF Core/Npgsql MUST NOT присутствовать).</summary>
    private static readonly string[] ForbiddenPackagePrefixes =
    {
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
    };

    /// <summary>Запрещённые подстроки текстового поиска (FR-001/FR-024).</summary>
    private static readonly string[] ForbiddenTerms =
    {
        "EntityFrameworkCore",
        "Npgsql",
    };

    /// <summary>Файлы проекта src/api из given кейса: приложение и его тесты.</summary>
    private static readonly string[] ProjectFiles =
    [
        RepoPaths.LabsAppProjectPath,
        Path.Combine(RepoPaths.SrcApiDirectory, "LabsApp.Tests", "LabsApp.Tests.csproj"),
    ];

    [Fact]
    public void BothProjectFiles_TargetNet80WithoutForbiddenPackageReferences()
    {
        foreach (var projectPath in ProjectFiles)
        {
            // given: файл проекта src/api.
            Assert.True(File.Exists(projectPath), $"Файл проекта не найден: {projectPath}.");
            var document = XDocument.Load(projectPath);

            // then: TargetFramework = net8.0.
            var targetFrameworks = document
                .Descendants("TargetFramework")
                .Select(element => element.Value)
                .ToList();
            Assert.True(
                targetFrameworks.Count == 1 && targetFrameworks[0] == "net8.0",
                $"{projectPath}: ожидался единственный <TargetFramework>net8.0</TargetFramework>, фактически: "
                + $"[{string.Join(", ", targetFrameworks)}].");

            // then: отсутствуют пакеты Microsoft.EntityFrameworkCore* и Npgsql*.
            var forbiddenReferences = document
                .Descendants("PackageReference")
                .Select(package => (string?)package.Attribute("Include") ?? string.Empty)
                .Where(include => ForbiddenPackagePrefixes.Any(
                    prefix => include.StartsWith(prefix, StringComparison.Ordinal)))
                .ToList();
            Assert.True(
                forbiddenReferences.Count == 0,
                $"{projectPath}: найдены запрещённые PackageReference: "
                + $"[{string.Join(", ", forbiddenReferences)}].");
        }
    }

    [Fact]
    public void SourcesAndProjectFiles_ContainNoEntityFrameworkCoreOrNpgsql()
    {
        // when: текстовый поиск 'EntityFrameworkCore'/'Npgsql' по исходникам
        // приложения (src/api/LabsApp, *.cs, без bin/obj) и файлам проекта
        // src/api (включая csproj).
        var files = Directory
            .EnumerateFiles(
                Path.Combine(RepoPaths.SrcApiDirectory, "LabsApp"),
                "*.cs",
                SearchOption.AllDirectories)
            .Concat(ProjectFiles)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(files.Count > 0, "Исходники src/api/LabsApp не найдены.");

        var violations = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                foreach (var term in ForbiddenTerms)
                {
                    if (lines[lineIndex].Contains(term, StringComparison.Ordinal))
                    {
                        violations.Add($"{file}:{lineIndex + 1}: «{term}»");
                    }
                }
            }
        }

        // then: 0 вхождений — EF Core/Npgsql отсутствуют в коде и файлах проекта
        // (FR-001 AC «Нет запрещённых зависимостей», FR-024 AC «Нет утечки EF»).
        Assert.True(
            violations.Count == 0,
            "Найдены запрещённые вхождения (EF Core / Npgsql): "
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
