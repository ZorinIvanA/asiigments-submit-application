using LabsApp.Domain.Entities;
using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Текущий пользователь — ответ register/login/me (IF-AUTH). Транспортная форма
/// дословно повторяет MeDto клиента (src/client/app/shared/models.ts, ASM-005):
/// id-поля — uuid-строки, null-поля присутствуют в JSON всегда (FR-007).
/// </summary>
public sealed class MeDto
{
    /// <summary>Логин.</summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;

    /// <summary>ФИО.</summary>
    [JsonPropertyName("fullName")]
    public string FullName { get; init; } = string.Empty;

    /// <summary>Роль: «student» | «teacher» (UserRoles).</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = UserRoles.Student;

    /// <summary>Имя группы; null = без группы или роль teacher.</summary>
    [JsonPropertyName("groupName")]
    public string? GroupName { get; init; }
}
