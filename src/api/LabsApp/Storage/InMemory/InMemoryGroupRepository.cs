using LabsApp.Domain;
using LabsApp.Domain.Entities;

namespace LabsApp.Storage.InMemory;

/// <summary>
/// In-memory реализация <see cref="IGroupRepository"/> (FR-024): Dictionary + ci-индекс
/// имени (ключ <see cref="Collation.Key"/>, сравнение Ordinal), составные операции
/// атомарны под общим <see cref="StorageLock"/> (IF-015). Delete выполняет каскад «сначала группа, затем пользователи»
/// под общей блокировкой. Хранилище хранит и отдаёт КОПИИ-снимки сущностей.
/// Реализация асинхронна по контракту интерфейса, но не выполняет ввода-вывода:
/// все задачи завершаются синхронно (Task.FromResult).
/// </summary>
public sealed class InMemoryGroupRepository(
    StorageLock lockObject,
    InMemoryUserRepository users) : IGroupRepository
{
    private readonly Dictionary<Guid, Group> _groups = new();
    private readonly Dictionary<string, Guid> _byName = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task AddAsync(Group group)
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

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Group?> GetByIdAsync(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult(_groups.TryGetValue(id, out var group) ? Clone(group) : null);
        }
    }

    /// <inheritdoc/>
    public Task<Group?> GetByNameAsync(string name)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult(_byName.TryGetValue(Collation.Key(name), out var id)
                ? Clone(_groups[id])
                : null);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Group>> GetAllAsync()
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<Group>>(_groups.Values.Select(Clone).ToList());
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Group>> ListAsync() => GetAllAsync();

    /// <inheritdoc/>
    public Task<bool> ExistsNameCiAsync(string name)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult(_byName.ContainsKey(Collation.Key(name)));
        }
    }

    /// <inheritdoc/>
    public Task UpdateAsync(Group group)
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

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            // Фиксированный порядок каскада (data_design): сначала запись группы,
            // затем сброс GroupId у её студентов.
            if (!_groups.Remove(id, out var group))
            {
                return Task.CompletedTask;
            }

            _byName.Remove(Collation.Key(group.Name));
            users.ClearGroupMembership(id);
        }

        return Task.CompletedTask;
    }

    private static Group Clone(Group group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        CreatedAt = group.CreatedAt,
    };
}
