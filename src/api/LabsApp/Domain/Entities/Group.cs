namespace LabsApp.Domain.Entities;

/// <summary>Доменная сущность Group (domain_model): учебная группа студентов.</summary>
public sealed class Group
{
    /// <summary>UUID v4, генерируется сервером.</summary>
    public Guid Id { get; set; }

    /// <summary>1–100 символов после трима; уникально ci (Collation).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Момент создания, UTC (DateTime.Kind = Utc, ADR-005).</summary>
    public DateTime CreatedAt { get; set; }
}
