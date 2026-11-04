namespace LabsApp.Hosting.Configuration;

/// <summary>
/// Параметры хранилища: строка подключения к PostgreSQL — единственный
/// провайдер приложения. Переменная: Storage__ConnectionString.
/// Тестовые двойки хранилища живут в tests/TestInfrastructure и в приложении
/// не регистрируются.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string ConnectionStringVariable = "Storage__ConnectionString";

    /// <summary>
    /// Dev-умолчание строки подключения — пользовательский кластер из src/api/db/README.md.
    /// В Production запрещено (guard StorageOptionsValidator) — требуется явное значение.
    /// </summary>
    public const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5434;Database=labsapp;Username=labsapp;Password=labsapp";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
