namespace LabsApp.Hosting.Configuration;

/// <summary>
/// Параметры хранилища (итерация «БД»): выбор провайдера и строка подключения
/// к PostgreSQL. Переменные: Storage__Provider (postgres — умолчание, inmemory —
/// тестовый дубль без ввода-вывода) и Storage__ConnectionString.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string ProviderVariable = "Storage__Provider";

    public const string ConnectionStringVariable = "Storage__ConnectionString";

    /// <summary>Провайдер PostgreSQL (EF Core + Npgsql) — умолчание.</summary>
    public const string PostgresProvider = "postgres";

    /// <summary>in-memory провайдер — тестовый дубль хранилища.</summary>
    public const string InMemoryProvider = "inmemory";

    /// <summary>
    /// Dev-умолчание строки подключения — пользовательский кластер из src/api/db/README.md.
    /// В Production запрещено (guard StorageOptionsValidator) — требуется явное значение.
    /// </summary>
    public const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5434;Database=labsapp;Username=labsapp;Password=labsapp";

    public string Provider { get; set; } = PostgresProvider;

    public string ConnectionString { get; set; } = DefaultConnectionString;

    /// <summary>Нормализованное имя провайдера (трим + нижний регистр).</summary>
    public string ResolveProvider() =>
        string.IsNullOrWhiteSpace(Provider) ? PostgresProvider : Provider.Trim().ToLowerInvariant();

    public bool IsPostgres => ResolveProvider() == PostgresProvider;

    public bool IsInMemory => ResolveProvider() == InMemoryProvider;
}
