using LabsApp.Domain.Entities;

namespace LabsApp.Storage;

/// <summary>
/// Репозиторий групп (IF-015, FR-024). ci-уникальность Name — по правилу
/// коллации <see cref="Domain.Collation"/>; составные операции атомарны (IF-015).
/// Контракт асинхронный (реализации могут выполнять ввод-вывод).
/// </summary>
public interface IGroupRepository
{
    /// <summary>
    /// Атомарно проверяет ci-уникальность Name и вставляет запись;
    /// конфликт — <see cref="StorageConflictException"/>.
    /// </summary>
    Task AddAsync(Group group);

    /// <summary>Группа по uuid либо null.</summary>
    Task<Group?> GetByIdAsync(Guid id);

    /// <summary>Группа по имени без учёта регистра либо null.</summary>
    Task<Group?> GetByNameAsync(string name);

    /// <summary>Все группы (порядок не определён; сортировка name↑ — в сервисе, FR-019).</summary>
    Task<IReadOnlyList<Group>> GetAllAsync();

    /// <summary>
    /// Все группы (IF-015, FR-019); порядок не определён — сортировка name↑ (русская
    /// локаль, AR-005) выполняется в сервисе. Семантически совпадает с
    /// <see cref="GetAllAsync"/>: контрактное имя для задач-потребителей групп.
    /// </summary>
    Task<IReadOnlyList<Group>> ListAsync();

    /// <summary>
    /// Занято ли имя группы по ci-правилу коллации (<see cref="Domain.Collation"/>,
    /// trim+lower-invariant+ordinal, ADR-013): true — существует группа с ci-равным
    /// именем. Используется контроллером для конфликта 409 при создании/переименовании.
    /// </summary>
    Task<bool> ExistsNameCiAsync(string name);

    /// <summary>
    /// Атомарно заменяет запись по <see cref="Group.Id"/> (запись обязана существовать —
    /// иначе <see cref="InvalidOperationException"/>) с перепривязкой ci-индекса;
    /// конфликт Name с другой группой — <see cref="StorageConflictException"/>.
    /// </summary>
    Task UpdateAsync(Group group);

    /// <summary>
    /// Каскадное удаление: запись группы удаляется, затем GroupId её студентов
    /// сбрасывается в null (ON DELETE SET NULL). Отсутствующая группа — отсутствие
    /// операции (идемпотентно).
    /// </summary>
    Task DeleteAsync(Guid id);
}
