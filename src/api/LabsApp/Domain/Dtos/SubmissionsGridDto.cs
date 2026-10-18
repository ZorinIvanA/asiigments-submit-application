using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Ведомость группы по семестру — ответ GET /submissions (IF-SUBMISSIONS, FR-021).
/// Транспортная форма дословно повторяет SubmissionsGridDto клиента (ASM-005);
/// дополнительно page — НОРМАЛИЗОВАННЫЙ номер страницы (нормализация значений
/// page &lt; 1/нечисловых — к 1, эхо запрещено).
/// </summary>
public sealed class SubmissionsGridDto
{
    /// <summary>Страница студентов группы (fullName↑, затем login↑ — Collation).</summary>
    [JsonPropertyName("students")]
    public IReadOnlyList<GridStudentDto> Students { get; init; } = [];

    /// <summary>Работы семестра, упорядочены по number по возрастанию.</summary>
    [JsonPropertyName("labs")]
    public IReadOnlyList<GridLabDto> Labs { get; init; } = [];

    /// <summary>Записи сдач для пар студент-работа текущей страницы.</summary>
    [JsonPropertyName("submissions")]
    public IReadOnlyList<GridSubmissionDto> Submissions { get; init; } = [];

    /// <summary>Общее число студентов группы (для подписи пагинации).</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>НОРМАЛИЗОВАННЫЙ номер страницы студентов (1-based).</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; }
}

/// <summary>Строка студента ведомости (проекция User: id + ФИО).</summary>
public sealed class GridStudentDto
{
    /// <summary>uuid студента.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>ФИО.</summary>
    [JsonPropertyName("fullName")]
    public string FullName { get; init; } = string.Empty;
}

/// <summary>Колонка работы ведомости (id + номер + признак защиты); используется
/// также в MySubmissionsDto (та же транспортная форма labs клиента).</summary>
public sealed class GridLabDto
{
    /// <summary>uuid работы.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Номер работы.</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }

    /// <summary>Признак «Нужна защита».</summary>
    [JsonPropertyName("defenseRequired")]
    public bool DefenseRequired { get; init; }
}

/// <summary>Запись сдачи в ведомости (без id/updatedBy — табличная проекция).</summary>
public sealed class GridSubmissionDto
{
    /// <summary>uuid студента.</summary>
    [JsonPropertyName("studentId")]
    public string StudentId { get; init; } = string.Empty;

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
