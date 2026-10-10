namespace LabsApp.Domain.Entities;

/// <summary>Доменная сущность Lab (domain_model): лабораторная работа.</summary>
public sealed class Lab
{
    /// <summary>UUID v4, генерируется сервером.</summary>
    public Guid Id { get; set; }

    /// <summary>Номер семестра курса: целое 1..Labs__MaxSemester включительно.</summary>
    public int Semester { get; set; }

    /// <summary>Номер работы: целое &gt; 0 (верхней границы нет); пара (semester, number) уникальна.</summary>
    public int Number { get; set; }

    /// <summary>Содержание работы, 1–500 символов после трима.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Ссылка на задание: null = ссылки нет; при наличии начинается с http:// или https://,
    /// длина 1–1000 символов после трима (FR-017, ISS-015).
    /// </summary>
    public string? AssignmentUrl { get; set; }

    /// <summary>Признак «Нужна защита» — строго boolean.</summary>
    public bool DefenseRequired { get; set; }

    /// <summary>Момент создания, UTC (DateTime.Kind = Utc, ADR-005).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Момент последнего обновления, UTC; меняется при обновлении записи.</summary>
    public DateTime UpdatedAt { get; set; }
}
