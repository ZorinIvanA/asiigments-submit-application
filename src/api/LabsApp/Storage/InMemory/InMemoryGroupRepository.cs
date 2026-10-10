using LabsApp.Domain;
using LabsApp.Domain.Entities;

namespace LabsApp.Storage.InMemory;

/// <summary>
/// In-memory реализация <see cref="IGroupRepository"/> (FR-024): Dictionary + ci-индекс
/// имени (ключ <see cref="Collation.Key"/>, сравнение Ordinal), составные операции
/// атомарны под общим <see cref="StorageLock"/> (IF-015). Delete выполняет каскад «сначала группа, затем пользователи»
/// под общей блокировкой. Хранилище хранит и отдаёт КОПИИ-снимки сущностей.
/// </summary>
public sealed class InMemoryGroupRepository(
    StorageLock lockObject,
    InMemoryUserRepository users) : IGroupRepository
{
    private readonly Dictionary<Guid, Group> _groups = new();
    private readonly Dictionary<string, Guid> _byName = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Add(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        lock (lockObject.SyncRoot)
        {
            var nameKey = Collation.Key(group.Name);

            if (_byName.ContainsKey(nameKey))
            {
                throw new StorageConflictException("Группа с таким названием уже существует.");
            }

            _groups[group.Id] = Clone(group);
            _byName[nameKey] = group.Id;
        }
    }

    /// <inheritdoc/>
    public Group? GetById(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            return _groups.TryGetValue(id, out var group) ? Clone(group) : null;
        }
    }

    /// <inheritdoc/>
    public Group? GetByName(string name)
    {
        lock (lockObject.SyncRoot)
        {
            return _byName.TryGetValue(Collation.Key(name), out var id)
                ? Clone(_groups[id])
                : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Group> GetAll()
    {
        lock (lockObject.SyncRoot)
        {
            return _groups.Values.Select(Clone).ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Group> List() => GetAll();

    /// <inheritdoc/>
    public bool ExistsNameCi(string name)
    {
        lock (lockObject.SyncRoot)
        {
            return _byName.ContainsKey(Collation.Key(name));
        }
    }

    /// <inheritdoc/>
    public void Update(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        lock (lockObject.SyncRoot)
        {
            if (!_groups.TryGetValue(group.Id, out var existing))
            {
                throw new InvalidOperationException("Группа не найдена.");
            }

            var nameKey = Collation.Key(group.Name);

            if (_byName.TryGetValue(nameKey, out var nameOwner) && nameOwner != group.Id)
            {
                throw new StorageConflictException("Группа с таким названием уже существует.");
            }

            _byName.Remove(Collation.Key(existing.Name));
            _groups[group.Id] = Clone(group);
            _byName[nameKey] = group.Id;
        }
    }

    /// <inheritdoc/>
    public void Delete(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            // Фиксированный порядок каскада (data_design): сначала запись группы,
            // затем сброс GroupId у её студентов.
            if (!_groups.Remove(id, out var group))
            {
                return;
            }

            _byName.Remove(Collation.Key(group.Name));
            users.ClearGroupMembership(id);
        }
    }

    private static Group Clone(Group group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        CreatedAt = group.CreatedAt,
    };
}
