using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Сдачи текущего студента по семестру — ответ GET /me/submissions (IF-SUBMISSIONS,
/// FR-023). Транспортная форма дословно повторяет MySubmissionsDto клиента (ASM-005).
/// </summary>
public sealed class MySubmissionsDto
{
    /// <summary>false = студент не включён в группу; при false массивы ниже пустые.</summary>
    [JsonPropertyName("hasGroup")]
    public bool HasGroup { get; init; }

    /// <summary>Работы семестра, упорядочены по number по возрастанию.</summary>
    [JsonPropertyName("labs")]
    public IReadOnlyList<GridLabDto> Labs { get; init; } = [];

    /// <summary>Сдачи только текущего студента (без studentId — он известен из сессии).</summary>
    [JsonPropertyName("submissions")]
    public IReadOnlyList<MySubmissionDto> Submissions { get; init; } = [];
}

/// <summary>Сдача текущего студента (labId + даты, без studentId).</summary>
public sealed class MySubmissionDto
{
    /// <summary>uuid работы.</summary>
    [JsonPropertyName("labId")]
    public string LabId { get; init; } = string.Empty;

    /// <summary>Дата сдачи 'YYYY-MM-DD' либо null.</summary>
    [JsonPropertyName("submitDate")]
    public DateOnly? SubmitDate { get; init; }

    /// <summary>Дата защиты 'YYYY-MM-DD' либо null.</summary>
    [JsonPropertyName("defenseDate")]
    public DateOnly? DefenseDate { get; init; }
}
