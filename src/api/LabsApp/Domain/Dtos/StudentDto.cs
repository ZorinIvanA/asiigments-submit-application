using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Строка списка студентов (GET /students, GET /groups/{id}/students — IF-STUDENTS/
/// IF-GROUPS). Проекция User без пароля и служебных полей; транспортная форма дословно
/// повторяет StudentDto клиента (ASM-005).
/// </summary>
public sealed class StudentDto
{
    /// <summary>uuid студента.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>ФИО.</summary>
    [JsonPropertyName("fullName")]
    public string FullName { get; init; } = string.Empty;

    /// <summary>Логин.</summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;

    /// <summary>Email.</summary>
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    /// <summary>Идентификатор текущей группы (uuid) либо null = без группы.</summary>
    [JsonPropertyName("groupId")]
    public string? GroupId { get; init; }

    /// <summary>Имя текущей группы либо null = без группы.</summary>
    [JsonPropertyName("groupName")]
    public string? GroupName { get; init; }
}
