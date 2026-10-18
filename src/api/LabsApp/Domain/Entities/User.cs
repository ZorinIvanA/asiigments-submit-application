namespace LabsApp.Domain.Entities;

/// <summary>
/// Доменная сущность User (domain_model): учётная запись студента либо преподавателя.
/// passwordHash в API-ответах не отдаётся никогда (domain_model, NFR-005).
/// </summary>
public sealed class User
{
    /// <summary>UUID v4, генерируется сервером.</summary>
    public Guid Id { get; set; }

    /// <summary>1–100 символов после трима, только [A-Za-z0-9._-]; уникален ci (Collation).</summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>Формат логин@домен.зона, 1–254 символа после трима; уникален ci (Collation).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>PBKDF2-хэш пароля (IPasswordHasher); исходный пароль не хранится.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>ФИО, 1–200 символов после трима.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Роль: <see cref="UserRoles.Student"/> либо <see cref="UserRoles.Teacher"/>.</summary>
    public string Role { get; set; } = UserRoles.Student;

    /// <summary>Группа студента; null = без группы; у teacher всегда null (ON DELETE SET NULL).</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Момент создания, UTC (DateTime.Kind = Utc, ADR-005).</summary>
    public DateTime CreatedAt { get; set; }
}
