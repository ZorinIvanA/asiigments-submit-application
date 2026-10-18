namespace LabsApp.IntegrationTests.B19.Infrastructure;

/// <summary>
/// Пути к репозиторию, вычисляемые от тестовой сборки: поднимаемся по дереву
/// каталогов до корня репозитория (каталог с angular.json — он же корень клиента:
/// sourceRoot проекта «asiigments-submit-application» = src/client).
/// Зона проверок батча — src/client (TS-175..TS-180) и error-texts.ts (TS-196).
/// </summary>
public static class RepoPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Каталог исходников клиента src/client — зона проверок TS-175..TS-180.</summary>
    public static string ClientSrcDirectory => Path.Combine(RepositoryRoot, "src", "client");

    /// <summary>Каталог приложения клиента (src/client/app).</summary>
    public static string ClientAppDirectory => Path.Combine(ClientSrcDirectory, "app");

    /// <summary>Каталог мок-слоя src/client/app/mock (FR-026 п.1: удаляется целиком).</summary>
    public static string ClientMockDirectory => Path.Combine(ClientAppDirectory, "mock");

    /// <summary>app.config.ts — точка удаления provideAppInitializer(setupMockLayer) (TS-175).</summary>
    public static string AppConfigFile => Path.Combine(
        ClientAppDirectory, "core", "config", "app.config.ts");

    /// <summary>shared/models.ts — удаление mock-только экспортов (TS-179).</summary>
    public static string SharedModelsFile => Path.Combine(
        ClientAppDirectory, "shared", "models.ts");

    /// <summary>shared/validation/error-texts.ts — out_of_scope, не изменяется (TS-196).</summary>
    public static string ErrorTextsFile => Path.Combine(
        ClientAppDirectory, "shared", "validation", "error-texts.ts");

    /// <summary>Сервисы core — публичный контракт IF-014 (TS-178).</summary>
    public static string CoreServicesDirectory => Path.Combine(
        ClientAppDirectory, "core", "services");

    /// <summary>core/api-base-url.ts — InjectionToken API_BASE_URL='/api/v1' (TS-178).</summary>
    public static string ApiBaseUrlFile => Path.Combine(ClientAppDirectory, "core", "api-base-url.ts");

    /// <summary>core/auth-interceptor.ts — интерцептор авторизации (TS-178).</summary>
    public static string AuthInterceptorFile => Path.Combine(
        ClientAppDirectory, "core", "auth-interceptor.ts");

    /// <summary>core/http-errors.ts — ApiError-нормализация (TS-178).</summary>
    public static string HttpErrorsFile => Path.Combine(ClientAppDirectory, "core", "http-errors.ts");

    /// <summary>Проект бэкенда (Kestrel), запускаемый сценарием TS-180.</summary>
    public static string BackendProjectFile => Path.Combine(
        RepositoryRoot, "src", "api", "LabsApp", "LabsApp.csproj");

    /// <summary>Решение бэкенда src/api — dotnet build ворот TS-149 (NFR-002).</summary>
    public static string ApiSolutionFile => Path.Combine(
        RepositoryRoot, "src", "api", "LabsApp.sln");

    /// <summary>Csproj проекта реализации — фиксация TreatWarningsAsErrors (TS-149).</summary>
    public static string ApiProjectFile => Path.Combine(
        RepositoryRoot, "src", "api", "LabsApp", "LabsApp.csproj");

    /// <summary>Csproj проекта тестов реализации — фиксация TreatWarningsAsErrors и dotnet test (TS-149).</summary>
    public static string ApiTestsProjectFile => Path.Combine(
        RepositoryRoot, "src", "api", "LabsApp.Tests", "LabsApp.Tests.csproj");

    /// <summary>proxy.conf.json — прокси dev-сервера на Kestrel (TS-180, FR-026 п.5).</summary>
    public static string ProxyConfigFile => Path.Combine(ClientSrcDirectory, "proxy.conf.json");

    /// <summary>CLI Angular: node_modules/@angular/cli/bin/ng.js.</summary>
    public static string NgCliJs =>
        Path.Combine(RepositoryRoot, "node_modules", "@angular", "cli", "bin", "ng.js");

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
