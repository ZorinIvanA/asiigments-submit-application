using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Группа в ответах GET/POST/PUT /groups (IF-GROUPS). Транспортная форма дословно
/// повторяет GroupDto клиента (ASM-005); id — uuid-строка.
/// </summary>
public sealed class GroupDto
{
    /// <summary>uuid группы.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Название группы, 1–100 символов.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Вычисляемое число студентов группы (&gt;= 0).</summary>
    [JsonPropertyName("studentCount")]
    public int StudentCount { get; init; }
}
