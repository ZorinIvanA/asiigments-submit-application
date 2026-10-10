namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Пути к репозиторию для ворот TS-146, вычисляемые от тестовой сборки:
/// подъём по дереву каталогов до корня репозитория (каталог с angular.json).
/// Зона проверок — тест-проект бэкенда src/api/LabsApp.Tests (FR-027); зона
/// батча (tests/integration/B-17) в решение src/api не входит.
/// </summary>
public static class B17RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Тест-проект бэкенда — объект ворот TS-146 (dotnet test, exit 0).</summary>
    public static string ApiTestsProjectFile => Path.Combine(
        RepositoryRoot, "src", "api", "LabsApp.Tests", "LabsApp.Tests.csproj");

    /// <summary>Каталог исходников тест-проекта бэкенда (инспекция покрытия TS-146).</summary>
    public static string ApiTestsDirectory => Path.Combine(
        RepositoryRoot, "src", "api", "LabsApp.Tests");

    /// <summary>Исходник тест-проекта бэкенда по пути относительно LabsApp.Tests.</summary>
    public static string TestSource(params string[] relativePath)
    {
        var parts = new List<string> { ApiTestsDirectory };
        parts.AddRange(relativePath);
        return Path.Combine([.. parts]);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "angular.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Не найден корень репозитория (каталог с angular.json) выше {AppContext.BaseDirectory}.");
    }
}
