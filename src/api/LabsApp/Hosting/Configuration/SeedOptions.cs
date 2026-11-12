namespace LabsApp.Hosting.Configuration;

/// <summary>FR-006/FR-007: параметры сида Seed__*.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public const string TeacherLoginVariable = "Seed__TeacherLogin";
    public const string TeacherPasswordVariable = "Seed__TeacherPassword";
    public const string DemoDataVariable = "Seed__DemoData";

    public const string DefaultTeacherLogin = "teacher";
    public const string DefaultTeacherPassword = "teacher123!";

    public string TeacherLogin { get; set; } = DefaultTeacherLogin;

    public string TeacherPassword { get; set; } = DefaultTeacherPassword;

    /// <summary>
    /// Сырое значение Seed__DemoData: 'true'/'false' (без учёта регистра);
    /// null или иное значение — умолчание окружения (FR-007).
    /// </summary>
    public string? DemoData { get; set; }

    /// <summary>true, если DemoData — корректное 'true'/'false' (без учёта регистра и пробелов).</summary>
    public static bool IsKnownDemoDataValue(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Разрешает признак демо-набора (FR-007 v2.3): вне Development флаг ВСЕГДА
    /// трактуется как false — явное 'true' игнорируется (opt-in в Production удалён);
    /// в Development явное 'false' отключает демо-набор, любое иное значение
    /// (включая 'true', null и некорректное) даёт умолчание окружения — true.
    /// </summary>
    public bool ResolveDemoData(bool inDevelopment)
    {
        if (!inDevelopment)
        {
            return false;
        }

        return !string.Equals(DemoData?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }
}
