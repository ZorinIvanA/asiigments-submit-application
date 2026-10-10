namespace LabsApp.Domain.Entities;

/// <summary>Словарь значений роли User (domain_model: {student, teacher}).</summary>
public static class UserRoles
{
    /// <summary>Студент; через API регистрируется только эта роль (FR-007).</summary>
    public const string Student = "student";

    /// <summary>Преподаватель; создаётся только сидом (FR-004).</summary>
    public const string Teacher = "teacher";
}
