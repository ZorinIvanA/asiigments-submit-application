namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Пути к репозиторию, вычисляемые от тестовой сборки: поднимаемся по дереву
/// каталогов до каталога, содержащего src/api/LabsApp.sln
/// (образец — Infrastructure/RepoPaths зоны B-01).
/// </summary>
public static class RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Каталог src/api — рабочая зона FR-096 (README бэкенда).</summary>
    public static string SrcApiDirectory => Path.Combine(RepositoryRoot, "src", "api");

    /// <summary>Проверяемый TS-183 файл src/api/README.md.</summary>
    public static string SrcApiReadmePath => Path.Combine(SrcApiDirectory, "README.md");

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
