using Microsoft.Extensions.Options;

namespace LabsApp.Hosting.Configuration;

/// <summary>
/// Fail-fast параметров хранилища: строка подключения Storage__ConnectionString
/// обязательна, а в Production дополнительно запрещено dev-умолчание
/// (явная конфигурация).
/// </summary>
public sealed class StorageOptionsValidator(string environmentName) : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail(
                $"Переменная {StorageOptions.ConnectionStringVariable} обязательна "
                + "(хранилище приложения — PostgreSQL).");
        }

        var isProduction = string.Equals(
            environmentName, "Production", StringComparison.OrdinalIgnoreCase);
        if (isProduction
            && string.Equals(options.ConnectionString, StorageOptions.DefaultConnectionString, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Переменная {StorageOptions.ConnectionStringVariable} в Production не может быть равна "
                + "dev-умолчанию: задайте явную строку подключения.");
        }

        return ValidateOptionsResult.Success;
    }
}
