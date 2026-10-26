using Microsoft.Extensions.Options;

namespace LabsApp.Hosting.Configuration;

/// <summary>
/// Fail-fast параметров хранилища: Storage__Provider — postgres|inmemory;
/// для postgres строка подключения Storage__ConnectionString обязательна,
/// а в Production дополнительно запрещено dev-умолчание (явная конфигурация).
/// </summary>
public sealed class StorageOptionsValidator(string environmentName) : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (!options.IsPostgres && !options.IsInMemory)
        {
            return ValidateOptionsResult.Fail(
                $"Переменная {StorageOptions.ProviderVariable} должна иметь значение "
                + $"'{StorageOptions.PostgresProvider}' либо '{StorageOptions.InMemoryProvider}' "
                + $"(получено '{options.Provider}').");
        }

        if (!options.IsPostgres)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail(
                $"Переменная {StorageOptions.ConnectionStringVariable} обязательна при провайдере "
                + $"'{StorageOptions.PostgresProvider}'.");
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
