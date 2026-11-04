using LabsApp.Domain.Entities;

namespace LabsApp.Infrastructure;

/// <summary>
/// Репозиторий сдач (IF-015, FR-024). Уникальность пары (StudentId, LabId);
/// семантика дат — data_design (FR-021): null = сброс значения даты, запись
/// с обеими null-датами сохраняется. Составные операции атомарны (IF-015).
/// Контракт асинхронный (реализации могут выполнять ввод-вывод).
/// </summary>
public interface ISubmissionRepository
{
    /// <summary>
    /// Атомарный upsert по паре (StudentId, LabId): создание записи (новый uuid)
    /// либо полная замена обеих дат и меток (UpdatedBy/UpdatedAt из аргументов,
    /// Id существующей сохраняется). Обе даты null = сброс обеих дат; запись пары
    /// сохраняется (не удаляется — data_design, FR-021). Возвращает сохранённую
    /// запись (всегда не null). UpdatedBy/UpdatedAt проставляет сервис (data_design).
    /// </summary>
    Task<Submission?> UpsertAsync(
        Guid studentId,
        Guid labId,
        DateOnly? submitDate,
        DateOnly? defenseDate,
        Guid? updatedBy,
        DateTime updatedAt);

    /// <summary>Сдача пары (StudentId, LabId) либо null.</summary>
    Task<Submission?> GetByStudentAndLabAsync(Guid studentId, Guid labId);

    /// <summary>Сдачи перечисленных работ (для ведомости преподавателя); порядок не определён.</summary>
    Task<IReadOnlyList<Submission>> ListByLabIdsAsync(IReadOnlyCollection<Guid> labIds);

    /// <summary>Сдачи студента (для /me/submissions); порядок не определён.</summary>
    Task<IReadOnlyList<Submission>> ListByStudentAsync(Guid studentId);

    /// <summary>
    /// Сдачи для ведомости по явному набору пар (FR-021): декартово произведение
    /// <paramref name="studentIds"/> × <paramref name="labIds"/>; возвращаются ТОЛЬКО
    /// существующие записи запрошенных пар (порядок не определён). Пустой любой из
    /// наборов — пустой результат.
    /// </summary>
    Task<IReadOnlyList<Submission>> GetByPairsAsync(IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<Guid> labIds);

    /// <summary>
    /// Сдачи студента среди лабораторных семестра (FR-021, /me/submissions):
    /// проекция сдач студента на переданный набор лабораторных этого семестра
    /// (semesterLabIds — список лабораторных семестра из ILabRepository; хранилище
    /// сдач семестр не хранит). Порядок не определён; пустой набор лабораторных —
    /// пустой результат.
    /// </summary>
    Task<IReadOnlyList<Submission>> ListForStudentAndSemesterAsync(Guid studentId, IReadOnlyCollection<Guid> semesterLabIds);

    /// <summary>Удаление всех сдач работы (каскад удаления работы и явная операция).</summary>
    Task DeleteByLabIdAsync(Guid labId);
}
