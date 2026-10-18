using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Лабораторная работа в ответах labs CRUD и списков (IF-LABS). Транспортная форма
/// дословно повторяет LabDto клиента (ASM-005); id — uuid-строка.
/// </summary>
public sealed class LabDto
{
    /// <summary>uuid работы.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Номер семестра курса: целое 1..Labs__MaxSemester.</summary>
    [JsonPropertyName("semester")]
    public int Semester { get; init; }

    /// <summary>Номер работы: целое &gt; 0; пара (semester, number) уникальна.</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }

    /// <summary>Содержание работы, 1–500 символов.</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>Ссылка на задание (http/https) либо null = ссылки нет.</summary>
    [JsonPropertyName("assignmentUrl")]
    public string? AssignmentUrl { get; init; }

    /// <summary>Признак «Нужна защита».</summary>
    [JsonPropertyName("defenseRequired")]
    public bool DefenseRequired { get; init; }
}
