using LabsApp.Domain.Entities;

namespace LabsApp.Storage;

/// <summary>
/// Репозиторий сдач (IF-015, FR-024). Уникальность пары (StudentId, LabId);
/// семантика дат — data_design (FR-021): null = сброс значения даты, запись
/// с обеими null-датами сохраняется. Составные операции атомарны под общим
/// <see cref="StorageLock"/> (IF-015).
/// </summary>
public interface ISubmissionRepository
{
    /// <summary>
    /// Атомарный upsert по паре (StudentId, LabId) под lock: создание записи
    /// (новый uuid) либо полная замена обеих дат и меток (UpdatedBy/UpdatedAt
    /// из аргументов, Id существующей сохраняется). Обе даты null = сброс
    /// обеих дат; запись пары сохраняется (не удаляется — data_design, FR-021).
    /// Возвращает сохранённую запись (всегда не null).
    /// UpdatedBy/UpdatedAt проставляет сервис (data_design).
    /// </summary>
    Submission? Upsert(
        Guid studentId,
        Guid labId,
        DateOnly? submitDate,
        DateOnly? defenseDate,
        Guid? updatedBy,
        DateTime updatedAt);

    /// <summary>Сдача пары (StudentId, LabId) либо null.</summary>
    Submission? GetByStudentAndLab(Guid studentId, Guid labId);

    /// <summary>Сдачи перечисленных работ (для ведомости преподавателя); порядок не определён.</summary>
    IReadOnlyList<Submission> ListByLabIds(IReadOnlyCollection<Guid> labIds);

    /// <summary>Сдачи студента (для /me/submissions); порядок не определён.</summary>
    IReadOnlyList<Submission> ListByStudent(Guid studentId);

    /// <summary>
    /// Сдачи для ведомости по явному набору пар (FR-021): декартово произведение
    /// <paramref name="studentIds"/> × <paramref name="labIds"/>; возвращаются ТОЛЬКО
    /// существующие записи запрошенных пар (порядок не определён). Пустой любой из
    /// наборов — пустой результат.
    /// </summary>
    IReadOnlyList<Submission> GetByPairs(IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<Guid> labIds);

    /// <summary>
    /// Сдачи студента среди лабораторных семестра (FR-021, /me/submissions):
    /// проекция сдач студента на переданный набор лабораторных этого семестра
    /// (semesterLabIds — список лабораторных семестра из ILabRepository; хранилище
    /// сдач семестр не хранит). Порядок не определён; пустой набор лабораторных —
    /// пустой результат.
    /// </summary>
    IReadOnlyList<Submission> ListForStudentAndSemester(Guid studentId, IReadOnlyCollection<Guid> semesterLabIds);

    /// <summary>Удаление всех сдач работы (каскад удаления работы и явная операция).</summary>
    void DeleteByLabId(Guid labId);
}
