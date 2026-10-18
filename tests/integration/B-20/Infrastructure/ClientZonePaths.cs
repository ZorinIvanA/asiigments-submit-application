namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Пути к клиентской части (src/client) для сценариев TS-140..TS-145 батча
/// B-20 (канонические файлы клиентской зоны B-19 Ts175..Ts180 переименованы
/// на префиксы Ts140..Ts145 при переиздании реестра). Собственный класс путей
/// (образец — Infrastructure/RepoPaths зоны B-19; чужая зона недоступна для
/// ссылок — BL-001 BUG-001): файлы RepoPaths/NfrGatePaths зоны B-20, общие с
/// чужими сценариями TS-030/TS-148/TS-149/TS-181..TS-183, не редактируются.
/// </summary>
public static class ClientZonePaths
{
    /// <summary>Каталог исходников клиента src/client — зона проверок TS-140..TS-145.</summary>
    public static string ClientSrcDirectory => Path.Combine(RepoPaths.RepositoryRoot, "src", "client");

    /// <summary>Каталог приложения клиента (src/client/app).</summary>
    public static string ClientAppDirectory => Path.Combine(ClientSrcDirectory, "app");

    /// <summary>Каталог мок-слоя src/client/app/mock (FR-026 п.1: удалён целиком).</summary>
    public static string ClientMockDirectory => Path.Combine(ClientAppDirectory, "mock");

    /// <summary>app.config.ts — точка удаления provideAppInitializer(setupMockLayer) (TS-140).</summary>
    public static string AppConfigFile => Path.Combine(
        ClientAppDirectory, "core", "config", "app.config.ts");

    /// <summary>shared/models.ts — удаление mock-только экспортов (TS-145).</summary>
    public static string SharedModelsFile => Path.Combine(
        ClientAppDirectory, "shared", "models.ts");

    /// <summary>core/api-base-url.ts — InjectionToken API_BASE_URL='/api/v1' (TS-143).</summary>
    public static string ApiBaseUrlFile => Path.Combine(ClientAppDirectory, "core", "api-base-url.ts");

    /// <summary>core/auth-interceptor.ts — интерцептор авторизации (TS-143).</summary>
    public static string AuthInterceptorFile => Path.Combine(
        ClientAppDirectory, "core", "auth-interceptor.ts");

    /// <summary>core/http-errors.ts — ApiError-нормализация (TS-143).</summary>
    public static string HttpErrorsFile => Path.Combine(ClientAppDirectory, "core", "http-errors.ts");

    /// <summary>Проект бэкенда (Kestrel), запускаемый сценарием TS-144.</summary>
    public static string BackendProjectFile => Path.Combine(
        RepoPaths.RepositoryRoot, "src", "api", "LabsApp", "LabsApp.csproj");

    /// <summary>proxy.conf.json — прокси dev-сервера на Kestrel (TS-144, FR-026 п.5).</summary>
    public static string ProxyConfigFile => Path.Combine(ClientSrcDirectory, "proxy.conf.json");
}
