using Microsoft.Extensions.Options;

namespace LabsApp.Hosting.Configuration;

/// <summary>
/// Fail-fast FR-030: Labs__MaxSemester — целое число в диапазоне 1..100.
/// Сообщение об ошибке указывает имя переменной окружения.
/// </summary>
public sealed class LabsOptionsValidator : IValidateOptions<LabsOptions>
{
    public ValidateOptionsResult Validate(string? name, LabsOptions options)
    {
        if (options.MaxSemester is >= LabsOptions.MinMaxSemester and <= LabsOptions.MaxMaxSemester)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"Переменная {LabsOptions.MaxSemesterVariable} должна быть целым числом в диапазоне "
            + $"от {LabsOptions.MinMaxSemester} до {LabsOptions.MaxMaxSemester} (получено {options.MaxSemester}); "
            + $"значение по умолчанию — {LabsOptions.DefaultMaxSemester}.");
    }
}
