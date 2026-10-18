namespace LabsApp.IntegrationTests.B18.Infrastructure;

/// <summary>
/// Пути к бэкенд-части решения (src/api) для сквозной NFR-верификации
/// TS-181/TS-182 (FR-027: обязательные гейты dotnet test). Вычисления путей —
/// собственные (как RepoPaths зоны), без ссылок на проекты реализации:
/// проект зоны B-18 сознательно не включён в src/api/LabsApp.sln — изоляция
/// от параллельных батчей (урок BL-001 BUG-001).
/// </summary>
public static class BackendSuitePaths
{
    public static string RepositoryRoot => RepoPaths.RepositoryRoot;

    /// <summary>Каталог бэкенд-решения src/api.</summary>
    public static string ApiDirectory => Path.Combine(RepositoryRoot, "src", "api");

    /// <summary>Бэкенд-солюшн (given TS-182: «решение бэкенда собрано»).</summary>
    public static string SolutionFile => Path.Combine(ApiDirectory, "LabsApp.sln");

    /// <summary>Тестовый проект бэкенда (given TS-181: src/api/LabsApp.Tests).</summary>
    public static string TestsProjectFile => Path.Combine(ApiDirectory, "LabsApp.Tests", "LabsApp.Tests.csproj");

    /// <summary>Каталог исходников тестового проекта бэкенда.</summary>
    public static string TestsSourceDirectory => Path.Combine(ApiDirectory, "LabsApp.Tests");

    /// <summary>
    /// Все исходники тестового проекта бэкенда (*.cs) без генерируемых
    /// каталогов bin/obj и тестовых ассетов, в детерминированном порядке.
    /// </summary>
    public static IReadOnlyList<string> TestSourceFiles()
    {
        if (!Directory.Exists(TestsSourceDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(TestsSourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsIgnored(file))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsIgnored(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}TestAssets{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
