namespace LabsApp.IntegrationTests.B02.Infrastructure;

/// <summary>
/// Пути к репозиторию, вычисляемые от тестовой сборки: поднимаемся по дереву
/// каталогов до каталога, содержащего src/api/LabsApp.sln (по образцу зоны B-01).
/// </summary>
public static class RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Каталог src/api — рабочая зона процессных тестов (dotnet run).</summary>
    public static string SrcApiDirectory => Path.Combine(RepositoryRoot, "src", "api");

    /// <summary>Проект приложения для запуска процесса (FR-025 guard, TS-136).</summary>
    public static string LabsAppProjectPath => Path.Combine(SrcApiDirectory, "LabsApp", "LabsApp.csproj");

    /// <summary>Каталог исходников приложения src/api/LabsApp — scope-греп TS-154 (CORS).</summary>
    public static string LabsAppSourceDirectory => Path.Combine(SrcApiDirectory, "LabsApp");

    /// <summary>Каталог клиента src/client — scope-кейс TS-156 (клиентский словарь текстов).</summary>
    public static string SrcClientDirectory => Path.Combine(RepositoryRoot, "src", "client");

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
