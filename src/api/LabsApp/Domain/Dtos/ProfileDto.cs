using LabsApp.Domain.Entities;
using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Профиль текущего пользователя — ответ GET/PUT /me/profile (IF-PROFILE).
/// Транспортная форма дословно повторяет ProfileDto клиента (ASM-005).
/// </summary>
public sealed class ProfileDto
{
    /// <summary>Логин (не редактируется).</summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;

    /// <summary>Email.</summary>
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

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
