namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Пути к репозиторию, вычисляемые от тестовой сборки: поднимаемся по дереву
/// каталогов до каталога, содержащего src/api/LabsApp.sln (копия механики
/// Infrastructure/RepoPaths зоны B-20; чужие зоны недоступны для ссылок —
/// изоляция зон, BL-001).
/// </summary>
public static class B16RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Каталог src/api — рабочая зона FR-028 (README бэкенда).</summary>
    public static string SrcApiDirectory => Path.Combine(RepositoryRoot, "src", "api");

    /// <summary>Проверяемый TS-147 файл src/api/README.md.</summary>
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
