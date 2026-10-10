using LabsApp.IntegrationTests.B19.Infrastructure;

namespace LabsApp.IntegrationTests.B19.Scenarios;

/// <summary>
/// Кейс батча B-19: TS-171 «Клиент: ng test — exit 0, спеки не импортируют
/// мок-слой» — реализован этим классом (исторические ID: кейс раунда c-882
/// TS-142; сценарий контура TS-177).
/// TS-177 «Клиент: ng test проходит без мок-слоя» (happy_path, P0, FR-026 AC
/// «Тесты клиента», NFR-002 «ng test — exit 0», OQ-006).
/// given: зависимости установлены; мок-слой удалён; spec-файлы
///        src/client/app/testing/integration/** переработаны на
///        HttpTestingController или удалены;
/// when:  ng test --watch=false --browsers=ChromeHeadless в src/client;
///        поиск импортов 'app/mock' по *.spec.ts;
/// then:  Exit 0; ни один spec не импортирует app/mock (FR-026 AC
///        «Тесты клиента»; OQ-006).
/// </summary>
/// <remarks>
/// Класс в последовательной коллекции: ng test конкурирует с ng build (TS-176)
/// и ng serve (TS-180) за .angular-кэш, dist и CPU — параллельный запуск даёт
/// ложные нестабильные падения (урок BL-001). Статическая проверка импортов
/// сериализации не требует, но colocана с классом сценария.
/// </remarks>
[Collection(SerialNgProcessCollection.Name)]
public sealed class Ts177_ClientTestRunTests
{
    /// <summary>
    /// when/then: ng test --watch=false --browsers=ChromeHeadless → Exit 0.
    /// Команда запускается дословно (NFR-002).
    /// </summary>
    [Fact]
    public async Task NgTest_ExitsZero()
    {
        // given: зависимости установлены; мок-слой удалён (проверяется TS-175).

        // when: ng test --watch=false --browsers=ChromeHeadless.
        var run = await NgCli.RunNgAsync(
            "test --watch=false --browsers=ChromeHeadless",
            (int)TimeSpan.FromMinutes(20).TotalMilliseconds);

        // then: Exit 0.
        Assert.True(
            run.ExitCode == 0,
            $"then не выполнен: ng test завершился с кодом {run.ExitCode} "
            + $"(ожидался 0 — FR-026 AC «Тесты клиента», NFR-002)."
            + $"{Environment.NewLine}{run.OutputTail()}");
    }

    /// <summary>
    /// when/then: поиск импортов 'app/mock' по *.spec.ts → ни один spec не
    /// импортирует мок-слой. Относительные спецификаторы разрешаются в
    /// абсолютный путь — ловится и «app/mock/…», и «../../mock» без ложных
    /// срабатываний на сторонние модули (например, testing/mock-breakpoint-observer).
    /// </summary>
    [Fact]
    public void NoSpecFile_ImportsAppMock()
    {
        var mockDirectory = AddDirectorySeparator(Path.GetFullPath(RepoPaths.ClientMockDirectory));
        var violations = new List<string>();

        foreach (var specFile in EnumerateSpecFiles())
        {
            var content = File.ReadAllText(specFile);
            foreach (var pattern in ModuleSpecifierPatterns)
            {
                foreach (Match match in pattern.Matches(content))
                {
                    var specifier = match.Groups["specifier"].Value;
                    if (!ImportsAppMock(specFile, specifier, mockDirectory, out var resolvedDescription))
                    {
                        continue;
                    }

                    var line = content[..match.Index].Count(static ch => ch == '\n') + 1;
                    violations.Add(
                        $"{RelativePath(specFile)}:{line}: import '{specifier}'{resolvedDescription}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: найдены spec-файлы, импортирующие мок-слой (app/mock):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>Спецификаторы модулей: статические и динамические импорты.</summary>
    private static readonly Regex[] ModuleSpecifierPatterns =
    [
        new(@"\bfrom\s*['""](?<specifier>[^'""]+)['""]", RegexOptions.Compiled),
        new(@"\bimport\s*\(\s*['""](?<specifier>[^'""]+)['""]", RegexOptions.Compiled),
        new(@"\bimport\s*['""](?<specifier>[^'""]+)['""]", RegexOptions.Compiled),
    ];

    private static IEnumerable<string> EnumerateSpecFiles()
    {
        if (!Directory.Exists(RepoPaths.ClientSrcDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(
            RepoPaths.ClientSrcDirectory,
            "*.spec.ts",
            SearchOption.AllDirectories);
    }

    private static bool ImportsAppMock(string specFile, string specifier, string mockDirectory, out string description)
    {
        description = string.Empty;

        if (!specifier.StartsWith('.'))
        {
            // Непартикулярный импорт: только явное «app/mock» в самом спецификаторе.
            if (specifier.Contains("app/mock", StringComparison.Ordinal))
            {
                description = " (спецификатор содержит app/mock)";
                return true;
            }

            return false;
        }

        // Относительный импорт: разрешаем в абсолютный путь без требования
        // существования цели (удалённый каталог не должен импортироваться вовсе).
        var baseDirectory = Path.GetDirectoryName(specFile) ?? string.Empty;
        var resolved = Path.GetFullPath(Path.Combine(baseDirectory, specifier));
        if (!resolved.StartsWith(mockDirectory, StringComparison.Ordinal))
        {
            return false;
        }

        description = " (разрешается в src/client/app/mock)";
        return true;
    }

    private static string AddDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
    }

    private static string RelativePath(string absolutePath) =>
        Path.GetRelativePath(RepoPaths.RepositoryRoot, absolutePath);
}
