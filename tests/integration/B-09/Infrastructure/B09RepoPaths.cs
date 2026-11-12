namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Пути к репозиторию, вычисляемые от тестовой сборки: поднимаемся по дереву
/// каталогов до каталога, содержащего src/api/LabsApp.sln (по образцу зон B-01/B-02).
/// Используется кейсом TS-021 (инспекция README каталога src/api — FR-004/FR-028).
/// </summary>
public static class B09RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Каталог src/api — README этого каталога инспектирует TS-021.</summary>
    public static string SrcApiDirectory => Path.Combine(RepositoryRoot, "src", "api");

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
