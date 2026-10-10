namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// Пути репозитория для ворот TS-185/TS-191 (NFR-002), вычисляемые от тестовой
/// сборки: поднимаемся по дереву каталогов до корня (каталог с angular.json).
/// Копия механики RepoPaths зоны B-19 (чужая зона недоступна для ссылок,
/// BL-001 BUG-001).
/// </summary>
internal static class B21RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Решение бэкенда src/api (dotnet build — TS-191; dotnet test — TS-185).</summary>
    public static string ApiSolutionPath => Path.Combine(RepositoryRoot, "src", "api", "LabsApp.sln");

    /// <summary>Проект бэкенд-тестов src/api (дочерний dotnet test — TS-185).</summary>
    public static string ApiTestProjectPath =>
        Path.Combine(RepositoryRoot, "src", "api", "LabsApp.Tests", "LabsApp.Tests.csproj");

    /// <summary>CLI Angular: node_modules/@angular/cli/bin/ng.js (workspace — корень репозитория).</summary>
    public static string NgCliJs =>
        Path.Combine(RepositoryRoot, "node_modules", "@angular", "cli", "bin", "ng.js");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && directory is not null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "angular.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Не найден корень репозитория (каталог с angular.json) выше {AppContext.BaseDirectory}.");
    }
}
