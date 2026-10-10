using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LabsApp.Hosting.Configuration;

/// <summary>
/// Fail-fast FR-006: Auth__AccessTtlMinutes и Auth__RefreshTtlDays — целые &gt; 0
/// (любое окружение); Auth__Pbkdf2Iterations — целое от 1 до AuthOptions.MaxPbkdf2Iterations
/// (любое окружение); в Production Auth__JwtKey обязательна и содержит не менее 32
/// символов. В Development ключ необязателен — без него композиция-корень
/// подставляет эпизодический случайный ключ (Program.cs).
/// Сообщения об ошибке указывают имя переменной окружения.
/// </summary>
public sealed class AuthOptionsValidator(string environmentName) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        if (options.AccessTtlMinutes <= 0)
        {
            failures.Add(
                $"Переменная {AuthOptions.AccessTtlMinutesVariable} должна быть целым числом больше 0 "
                + $"(получено {options.AccessTtlMinutes}); значение по умолчанию — {AuthOptions.DefaultAccessTtlMinutes}.");
        }

        if (options.RefreshTtlDays <= 0)
        {
            failures.Add(
                $"Переменная {AuthOptions.RefreshTtlDaysVariable} должна быть целым числом больше 0 "
                + $"(получено {options.RefreshTtlDays}); значение по умолчанию — {AuthOptions.DefaultRefreshTtlDays}.");
        }

        if (options.Pbkdf2Iterations is < 1 or > AuthOptions.MaxPbkdf2Iterations)
        {
            failures.Add(
                $"Переменная {AuthOptions.Pbkdf2IterationsVariable} должна быть целым числом от 1 до "
                + $"{AuthOptions.MaxPbkdf2Iterations} (получено {options.Pbkdf2Iterations}); "
                + $"значение по умолчанию — {AuthOptions.DefaultPbkdf2Iterations}.");
        }

        if (IsProduction)
        {
            if (string.IsNullOrWhiteSpace(options.JwtKey))
            {
                failures.Add($"Переменная {AuthOptions.JwtKeyVariable} обязательна в окружении Production.");
            }
            else if (options.JwtKey.Length < AuthOptions.JwtKeyMinLength)
            {
                failures.Add(
                    $"Переменная {AuthOptions.JwtKeyVariable} в окружении Production должна содержать "
                    + $"не менее {AuthOptions.JwtKeyMinLength} символов (получено {options.JwtKey.Length}).");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private bool IsProduction =>
        string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);
}
