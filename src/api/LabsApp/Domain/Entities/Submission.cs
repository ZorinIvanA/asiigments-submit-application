namespace LabsApp.Domain.Entities;

/// <summary>
/// Доменная сущность Submission (domain_model): запись сдачи по паре (studentId, labId).
/// Семантика дат — data_design (FR-021): null = «не сдано»/«защита не проставлена»;
/// запись с обеими null-датами допустима и сохраняется (сброс дат не удаляет запись).
/// </summary>
public sealed class Submission
{
    /// <summary>UUID v4, генерируется сервером.</summary>
    public Guid Id { get; set; }

    /// <summary>Ссылка на User с role = student; пара (studentId, labId) уникальна.</summary>
    public Guid StudentId { get; set; }

    /// <summary>Ссылка на Lab; при удалении работы запись удаляется каскадно (domain_model).</summary>
    public Guid LabId { get; set; }

    /// <summary>Дата сдачи 'YYYY-MM-DD' либо null = не сдано.</summary>
    public DateOnly? SubmitDate { get; set; }

    /// <summary>Дата защиты 'YYYY-MM-DD' либо null = защита не проставлена.</summary>
    public DateOnly? DefenseDate { get; set; }

    /// <summary>UUID пользователя, последним изменившего запись (uuid преподавателя сессии).</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>Момент последнего изменения, UTC (DateTime.Kind = Utc, ADR-004).</summary>
    public DateTime UpdatedAt { get; set; }
}
