namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-161 «Репозитории: EF Core/Npgsql отсутствуют в src/api» (scope, P0,
/// FR-024 AC «Нет утечки EF», FR-001 AC «Нет запрещённых зависимостей»).
///
/// given: исходники и файлы проекта src/api (без каталогов bin/obj).
/// when:  поиск строк 'EntityFrameworkCore' и 'Npgsql' по коду и файлам проекта.
/// then:  0 вхождений.
///
/// Корпус: код и файлы проекта ПРИЛОЖЕНИЯ (src/api/LabsApp/** + LabsApp.csproj +
/// LabsApp.sln, без bin/obj). Источники сборки LabsApp.Tests в корпус не входят:
/// её санкционированный гейт-аудит (HostingIntegrationGateTests.ProjectAudit)
/// обязан цитировать маркеры как запрещённые литералы ([InlineData("Npgsql")]) —
/// факт прогона c-1217: 3 вхождения в LabsApp.Tests/Hosting/
/// HostingIntegrationGateTests.cs:27/320/341, все — цитаты самого аудита;
/// поэтому литеральный корпус «весь src/api» неисполним (дефект формулировки
/// кейса передан в scenario_change_requests). Запрет FR-024 («EF Core/Npgsql
/// MUST NOT использоваться») относится к системе — приложению.
/// </summary>
public sealed class Ts161_EfCoreNpgsqlZeroOccurrencesTests
{
    private static readonly string[] ScannedExtensions =
    [
        ".cs",
        ".csproj",
        ".sln",
        ".props",
        ".targets",
        ".json",
    ];

    [Fact]
    public void EntityFrameworkCoreAndNpgsql_HaveZeroOccurrencesInSourcesAndProjectFiles()
    {
        // given: исходники и файлы проекта src/api (без каталогов bin/obj; без
        // источников тестовой сборки — см. комментарий класса).
        var srcApiRoot = LocateSrcApiRoot();
        var corpus = Directory
            .EnumerateFiles(srcApiRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
                    && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase)
                    && !segments.Contains("LabsApp.Tests", StringComparer.OrdinalIgnoreCase)
                    && ScannedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Санит-стража корпуса: файл проекта LabsApp.csproj обязан быть в выборке —
        // иначе пустой корпус дал бы ложно-зелёный результат.
        Assert.Contains(
            corpus,
            path => path.EndsWith($"LabsApp{Path.DirectorySeparatorChar}LabsApp.csproj", StringComparison.Ordinal));

        // when: поиск строк 'EntityFrameworkCore' и 'Npgsql' по коду и файлам проекта.
        var efHits = new List<string>();
        var npgsqlHits = new List<string>();
        foreach (var path in corpus)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                // Нечитаемый в момент прогона файл пропускается (блокировка внешним
                // процессом) — корпус кода и файлов проекта от этого не пустеет.
                continue;
            }

            var lines = text.Split('\n');
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (lines[lineIndex].Contains("EntityFrameworkCore", StringComparison.Ordinal))
                {
                    efHits.Add($"{path}:{lineIndex + 1}");
                }

                if (lines[lineIndex].Contains("Npgsql", StringComparison.Ordinal))
                {
                    npgsqlHits.Add($"{path}:{lineIndex + 1}");
                }
            }
        }

        // then: 0 вхождений.
        Assert.True(
            efHits.Count == 0,
            "Ожидалось 0 вхождений 'EntityFrameworkCore' в src/api, найдены: " + string.Join(", ", efHits));
        Assert.True(
            npgsqlHits.Count == 0,
            "Ожидалось 0 вхождений 'Npgsql' в src/api, найдены: " + string.Join(", ", npgsqlHits));
    }

    /// <summary>
    /// Поднимается от выходного каталога тестовой сборки до корня репозитория
    /// (каталог, содержащий src/api/LabsApp/LabsApp.csproj).
    /// </summary>
    private static string LocateSrcApiRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; candidate is not null && depth < 12; depth++)
        {
            var probe = Path.Combine(candidate.FullName, "src", "api", "LabsApp", "LabsApp.csproj");
            if (File.Exists(probe))
            {
                return Path.Combine(candidate.FullName, "src", "api");
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            $"Корень репозитория с src/api/LabsApp/LabsApp.csproj не найден от {AppContext.BaseDirectory}.");
    }
}
