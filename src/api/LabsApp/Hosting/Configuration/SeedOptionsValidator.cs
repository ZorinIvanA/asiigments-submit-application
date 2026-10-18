using LabsApp.Domain.Validation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LabsApp.Hosting.Configuration;

/// <summary>
/// Fail-fast FR-006: в Production Seed__TeacherPassword должна быть задана явно,
/// отличаться от значения по умолчанию и удовлетворять правилам пароля §8
/// (единый валидатор <see cref="FieldValidators.Password"/>).
/// Валидируется СЫРОЕ значение без трима: §8 запрещает трим пароля, и сида
/// (<see cref="Storage.SeedRunner"/>) хэширует дословно ту же строку — guard и
/// хэширование работают с одной строкой (SEC-001).
/// Сообщение об ошибке указывает имя переменной окружения.
/// </summary>
public sealed class SeedOptionsValidator(string environmentName) : IValidateOptions<SeedOptions>
{
    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        if (!IsProduction)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.TeacherPassword))
        {
            return ValidateOptionsResult.Fail(
                $"Переменная {SeedOptions.TeacherPasswordVariable} обязательна в окружении Production.");
        }

        if (string.Equals(options.TeacherPassword, SeedOptions.DefaultTeacherPassword, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Переменная {SeedOptions.TeacherPasswordVariable} в окружении Production должна быть задана "
                + $"явно и отличаться от значения по умолчанию.");
        }

        var ruleFailures = FieldValidators.Password(options.TeacherPassword);
        if (ruleFailures.Count > 0)
        {
            return ValidateOptionsResult.Fail(
                $"Переменная {SeedOptions.TeacherPasswordVariable} в окружении Production должна "
                + $"удовлетворять правилам пароля (§8): {string.Join(" ", ruleFailures)}.");
        }

        return ValidateOptionsResult.Success;
    }

    private bool IsProduction =>
        string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);
}
