using LabsApp.Domain.Entities;

namespace LabsApp.Storage;

/// <summary>
/// Репозиторий лабораторных работ (IF-015, FR-002). Уникальность пары
/// (Semester, Number); составные операции атомарны (ADR-002).
/// </summary>
public interface ILabRepository
{
    /// <summary>
    /// Атомарно проверяет уникальность пары (Semester, Number) и вставляет запись;
    /// конфликт — <see cref="StorageConflictException"/>.
    /// </summary>
    void Add(Lab lab);

    /// <summary>Работа по uuid либо null.</summary>
    Lab? GetById(Guid id);

    /// <summary>Все работы (порядок не определён; сортировка — в сервисе, FR-014).</summary>
    IReadOnlyList<Lab> GetAll();

    /// <summary>
    /// Работы с фильтром семестра (IF-015, FR-017): <see langword="null"/> — без
    /// фильтра (все работы); значение — точное равенство <see cref="Lab.Semester"/>,
    /// в том числе внедиапазонное даёт пустую выборку (мягкая нормализация параметра
    /// и сортировка/пагинация — зона контроллера, ASM-018; порядок здесь не определён).
    /// </summary>
    IReadOnlyList<Lab> ListByFilter(int? semester);

    /// <summary>
    /// Различные номера семестров, по которым есть хотя бы одна работа, по возрастанию
    /// (IF-015, FR-018). Пустое хранилище — пустой список.
    /// </summary>
    IReadOnlyList<int> Semesters();

    /// <summary>Работа по паре (семестр, номер) либо null.</summary>
    Lab? TryGetByPair(int semester, int number);

    /// <summary>
    /// Занята ли пара (semester, number) (IF-015): true — существует работа с этой
    /// парой; <paramref name="exceptId"/> — запись, чья собственная пара конфликтом
    /// не считается (предпроверка 409 при обновлении, зеркально семантике Update).
    /// Читается под общей блокировкой — согласована с конкурентными Add/Update.
    /// </summary>
    bool ExistsPair(int semester, int number, Guid? exceptId = null);

    /// <summary>
    /// Атомарно заменяет запись по <see cref="Lab.Id"/> (запись обязана существовать —
    /// иначе <see cref="InvalidOperationException"/>) с перепривязкой индекса пары и
    /// простановкой UpdatedAt из TimeProvider (ADR-002/FR-003; data_design: updatedAt
    /// меняется при обновлении); конфликт пары с другой работой —
    /// <see cref="StorageConflictException"/>.
    /// </summary>
    void Update(Lab lab);

    /// <summary>
    /// Каскадное удаление под общей блокировкой: запись работы удаляется вместе со
    /// всеми её сдачами (ON DELETE CASCADE, domain_model). Отсутствующая работа —
    /// отсутствие операции (идемпотентно).
    /// </summary>
    void Delete(Guid id);
}
