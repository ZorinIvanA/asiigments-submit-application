namespace LabsApp.IntegrationTests.B18.Infrastructure;

/// <summary>
/// Пути к репозиторию, вычисляемые от тестовой сборки: поднимаемся по дереву
/// каталогов до КОРНЯ РЕПОЗИТОРИЯ (angular.json лежит в корне репозитория,
/// рядом с src/ и tests/, а не в каталоге клиента).
/// </summary>
public static class RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Каталог исходников клиента — зона проверок TS-171/TS-175.</summary>
    public static string ClientSrcDirectory => Path.Combine(RepositoryRoot, "src", "client");

    /// <summary>Каталог приложения клиента (src/client/app).</summary>
    public static string ClientAppDirectory => Path.Combine(ClientSrcDirectory, "app");

    /// <summary>
    /// Каталог мок-слоя src/client/app/mock — клиентская чистка (зона
    /// проверок TS-171/TS-175) требует его отсутствия.
    /// </summary>
    public static string ClientMockDirectory => Path.Combine(ClientAppDirectory, "mock");

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
