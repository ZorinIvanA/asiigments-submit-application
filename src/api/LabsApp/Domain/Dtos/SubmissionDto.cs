using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Запись сдачи в ответе PUT /submissions (FR-061, IF-SUBMISSIONS). Null-формы не
/// существует: PUT всегда возвращает ПОЛНУЮ запись upsert — id/updatedAt/updatedBy
/// ненулевые (id — uuid записи, updatedAt — момент изменения TimeProvider, updatedBy —
/// uuid преподавателя сессии). submitDate/defenseDate остаются nullable: null = дата
/// сброшена. Даты — DateOnly: нативная сериализация 'YYYY-MM-DD' (ADR-005); updatedAt —
/// DateTime Utc (Kind = Utc) → ISO-8601 с суффиксом Z.
/// </summary>
public sealed class SubmissionDto
{
    /// <summary>uuid записи сдачи.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>uuid студента (как передан в запросе).</summary>
    [JsonPropertyName("studentId")]
    public string StudentId { get; init; } = string.Empty;

    /// <summary>uuid работы (как передан в запросе).</summary>
    [JsonPropertyName("labId")]
    public string LabId { get; init; } = string.Empty;

    /// <summary>Дата сдачи 'YYYY-MM-DD' либо null (сброс).</summary>
    [JsonPropertyName("submitDate")]
    public DateOnly? SubmitDate { get; init; }

    /// <summary>Дата защиты 'YYYY-MM-DD' либо null (сброс).</summary>
    [JsonPropertyName("defenseDate")]
    public DateOnly? DefenseDate { get; init; }

    /// <summary>Момент последнего изменения (ISO-8601, UTC, TimeProvider).</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }

    /// <summary>uuid преподавателя, выполнившего изменение.</summary>
    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; init; } = string.Empty;
}
