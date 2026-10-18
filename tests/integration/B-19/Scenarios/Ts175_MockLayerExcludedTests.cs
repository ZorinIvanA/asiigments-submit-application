using LabsApp.IntegrationTests.B19.Infrastructure;

namespace LabsApp.IntegrationTests.B19.Scenarios;

/// <summary>
/// Кейс батча B-19: TS-169 «Клиент: мок-слой исключён из кода и тестов» —
/// реализован этим классом (исторические ID: кейс раунда c-882 TS-140;
/// сценарий контура TS-175).
/// TS-175 «Клиент: мок-слой исключён из кода и сборки» (scope, P0, FR-026
/// AC «Мок-слой исключён»).
/// given: дерево src/client после миграции (текущее состояние репозитория);
/// when:  поиск строк 'app/mock' и 'setupMockLayer' по src/client
///        (код и spec-файлы);
/// then:  0 вхождений в компилируемом коде и тестах; в
///        src/client/app/core/config/app.config.ts нет import setupMockLayer
///        и provideAppInitializer(setupMockLayer)
///        (FR-026 AC «Мок-слой исключён»).
/// </summary>
public sealed class Ts175_MockLayerExcludedTests
{
    private static readonly string[] NeedleSubstrings =
    [
        "app/mock",
        "setupMockLayer",
    ];

    /// <summary>
    /// FR-026 п.1 (техрешение C-015): каталог src/client/app/mock удаляется
    /// целиком — given кейса прямо называет его «подлежащим удалению».
    /// </summary>
    [Fact]
    public void MockDirectory_IsRemovedFromClientTree()
    {
        Assert.True(
            !Directory.Exists(RepoPaths.ClientMockDirectory),
            "then не выполнен: каталог мок-слоя src/client/app/mock всё ещё существует "
            + "(FR-026 п.1: слой удалён; C-015: каталог удаляется целиком).");
    }

    /// <summary>
    /// when/then: поиск 'app/mock' и 'setupMockLayer' по src/client (включая
    /// spec-файлы) → 0 вхождений в компилируемом коде и тестах. Сканируются все
    /// *.ts под src/client; каждое вхождение репортируется файлом и строкой.
    /// </summary>
    [Fact]
    public void ClientSources_HaveNoAppMockOrSetupMockLayerOccurrences()
    {
        var violations = new List<string>();

        foreach (var sourceFile in EnumerateClientSourceFiles("*.ts"))
        {
            var content = File.ReadAllText(sourceFile);
            foreach (var needle in NeedleSubstrings)
            {
                var offset = 0;
                while (true)
                {
                    var found = content.IndexOf(needle, offset, StringComparison.Ordinal);
                    if (found < 0)
                    {
                        break;
                    }

                    var line = content[..found].Count(static ch => ch == '\n') + 1;
                    violations.Add($"{RelativePath(sourceFile)}:{line}: '{needle}'");
                    offset = found + needle.Length;
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: найдены вхождения 'app/mock'/'setupMockLayer' в коде и тестах клиента:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// then: из app.config.ts удалены import setupMockLayer и
    /// provideAppInitializer(setupMockLayer). Проверяются оба маркера дословно,
    /// плюс любой import-спецификатор, указывающий в mock.
    /// </summary>
    [Fact]
    public void AppConfig_HasNoSetupMockLayerWiring()
    {
        Assert.True(
            File.Exists(RepoPaths.AppConfigFile),
            $"given не выполнен: не найден {RelativePath(RepoPaths.AppConfigFile)}.");

        var content = File.ReadAllText(RepoPaths.AppConfigFile);
        var violations = new List<string>();

        if (content.Contains("setupMockLayer", StringComparison.Ordinal))
        {
            violations.Add("осталось вхождение 'setupMockLayer' (import и/или инициализатор)");
        }

        if (content.Contains("provideAppInitializer(setupMockLayer)", StringComparison.Ordinal))
        {
            violations.Add("остался вызов provideAppInitializer(setupMockLayer)");
        }

        var mockImports = Regex.Matches(content, @"from\s*['""](?<specifier>[^'""]*mock[^'""]*)['""]");
        foreach (Match match in mockImports)
        {
            violations.Add($"остался import из '{match.Groups["specifier"].Value}'");
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: app.config.ts всё ещё подключает мок-слой:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    private static IEnumerable<string> EnumerateClientSourceFiles(string searchPattern)
    {
        if (!Directory.Exists(RepoPaths.ClientSrcDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(
            RepoPaths.ClientSrcDirectory,
            searchPattern,
            SearchOption.AllDirectories);
    }

    private static string RelativePath(string absolutePath) =>
        Path.GetRelativePath(RepoPaths.RepositoryRoot, absolutePath);
}
