namespace LabsApp.IntegrationTests.B01.Infrastructure;

/// <summary>
/// Пути к репозиторию, вычисляемые от тестовой сборки: поднимаемся по дереву
/// каталогов до каталога, содержащего src/api/LabsApp.sln.
/// </summary>
public static class RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Каталог src/api — зона статического анализа кейса TS-006.</summary>
    public static string SrcApiDirectory => Path.Combine(RepositoryRoot, "src", "api");

    /// <summary>Файл проекта приложения (статический анализ кейса TS-006).</summary>
    public static string LabsAppProjectPath => Path.Combine(SrcApiDirectory, "LabsApp", "LabsApp.csproj");

    /// <summary>Тестовый wwwroot, скопированный из TestAssets csproj-целью.</summary>
    public static string TestWwwroot => Path.Combine(AppContext.BaseDirectory, "wwwroot");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "api", "LabsApp.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Не найден корень репозитория (каталог с src/api/LabsApp.sln) выше {AppContext.BaseDirectory}.");
    }
}
