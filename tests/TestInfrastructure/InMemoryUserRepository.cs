using LabsApp.Domain;
using LabsApp.Domain.Entities;

using LabsApp.Infrastructure;

namespace LabsApp.TestInfrastructure;

/// <summary>
/// In-memory реализация <see cref="IUserRepository"/> (FR-024): Dictionary + индексы
/// ci-уникальности login/email (ключи <see cref="Collation.Key"/>, сравнение Ordinal),
/// составные операции атомарны под общим <see cref="StorageLock"/> (IF-015). Каждая составная операция — целиком в
/// критической секции. Хранилище хранит и отдаёт КОПИИ-снимки сущностей: внешняя
/// мутация выданной копии не влияет на хранилище и ci-индексы, изменения — только
/// через узкие атомарные мутаторы <see cref="SetGroupAsync"/> (группа, SEC-001),
/// <see cref="UpdateProfileAsync"/> (профиль) —
/// каждый меняет ТОЛЬКО свои поля записи и не откатывает конкурентные мутации
/// соседних полей (CR-001); <see cref="UpdateAsync"/> — полная замена, прод-контроллерами
/// не применяется (документально суженный контракт, OQ-002).
/// Реализация асинхронна по контракту интерфейса, но не выполняет ввода-вывода:
/// все задачи завершаются синхронно (Task.FromResult).
/// </summary>
public sealed class InMemoryUserRepository(StorageLock lockObject) : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();
    private readonly Dictionary<string, Guid> _byLogin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _byEmail = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task AddAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (lockObject.SyncRoot)
        {
            var loginKey = Collation.Key(user.Login);
            var emailKey = Collation.Key(user.Email);

            if (_byLogin.ContainsKey(loginKey))
            {
                throw new StorageConflictException("Пользователь с таким логином уже существует.");
            }

            if (_byEmail.ContainsKey(emailKey))
            {
                throw new StorageConflictException("Пользователь с таким email уже существует.");
            }

            _users[user.Id] = Clone(user);
            _byLogin[loginKey] = user.Id;
            _byEmail[emailKey] = user.Id;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<User?> GetByIdAsync(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult(_users.TryGetValue(id, out var user) ? Clone(user) : null);
        }
    }

    /// <inheritdoc/>
    public Task<User?> GetByLoginAsync(string login)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult(_byLogin.TryGetValue(Collation.Key(login), out var id)
                ? Clone(_users[id])
                : null);
        }
    }

    /// <inheritdoc/>
    public Task<User?> GetByEmailAsync(string email)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult(_byEmail.TryGetValue(Collation.Key(email), out var id)
                ? Clone(_users[id])
                : null);
        }
    }

    /// <inheritdoc/>
    public Task UpdateAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (lockObject.SyncRoot)
        {
            if (!_users.TryGetValue(user.Id, out var existing))
            {
                throw new InvalidOperationException("Пользователь не найден.");
            }

            var loginKey = Collation.Key(user.Login);
            var emailKey = Collation.Key(user.Email);

            if (_byLogin.TryGetValue(loginKey, out var loginOwner) && loginOwner != user.Id)
            {
                throw new StorageConflictException("Пользователь с таким логином уже существует.");
            }

            if (_byEmail.TryGetValue(emailKey, out var emailOwner) && emailOwner != user.Id)
            {
                throw new StorageConflictException("Пользователь с таким email уже существует.");
            }

            // Старые ключи — из сохранённого снимка, а не из изменённого вызывающим кодом.
            _byLogin.Remove(Collation.Key(existing.Login));
            _byEmail.Remove(Collation.Key(existing.Email));
            _users[user.Id] = Clone(user);
            _byLogin[loginKey] = user.Id;
            _byEmail[emailKey] = user.Id;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<SetGroupResult> SetGroupAsync(Guid userId, Guid? groupId, Func<Guid, Task<bool>>? groupExists)
    {
        if (groupId is not null && groupExists is null)
        {
            throw new ArgumentNullException(nameof(groupExists));
        }

        lock (lockObject.SyncRoot)
        {
            // Узкая мутация (SEC-001): вся составная проверка и запись — в одной
            // критической секции, меняется ТОЛЬКО GroupId хранимой записи (без
            // перечитывания и перезаписи полного снимка), поэтому параллельная
            // смена пароля/профиля студента не откатывается, а группа, удалённая
            // после проверки, не оставляет висячего GroupId.
            if (!_users.TryGetValue(userId, out var user) || user.Role != UserRoles.Student)
            {
                return Task.FromResult(SetGroupResult.StudentNotFound);
            }

            if (groupId is { } target)
            {
                // groupExists обязателен для ненулевой цели — guard в начале метода.
                // Делегат асинхронный по контракту, но в этом хранилище завершается
                // синхронно (in-memory-задачи без ввода-вывода), поэтому блокирующее
                // ожидание под замком не защищает ничью асинхронную работу.
                if (!groupExists!(target).GetAwaiter().GetResult())
                {
                    return Task.FromResult(SetGroupResult.GroupNotFound);
                }
            }

            user.GroupId = groupId;
            return Task.FromResult(SetGroupResult.Success);
        }
    }

    /// <inheritdoc/>
    public Task<UpdateProfileResult> UpdateProfileAsync(Guid userId, string fullName, string email)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(email);

        lock (lockObject.SyncRoot)
        {
            // Узкая мутация (CR-001/SEC-001): проверка существования, авторитетная
            // ci-проверка занятости email и запись — ОДНА критическая секция (гонка
            // двух UpdateProfile на один email → ровно один Success). Меняются ТОЛЬКО
            // FullName и Email с перепривязкой byEmail; Login/Role/GroupId/
            // PasswordHash/CreatedAt не перечитываются и не перезаписываются.
            if (!_users.TryGetValue(userId, out var user))
            {
                return Task.FromResult(UpdateProfileResult.UserNotFound);
            }

            var newEmailKey = Collation.Key(email);
            if (_byEmail.TryGetValue(newEmailKey, out var owner) && owner != userId)
            {
                return Task.FromResult(UpdateProfileResult.EmailConflict);
            }

            var oldEmailKey = Collation.Key(user.Email);
            if (!oldEmailKey.Equals(newEmailKey, StringComparison.Ordinal))
            {
                _byEmail.Remove(oldEmailKey);
                _byEmail[newEmailKey] = userId;
            }

            user.FullName = fullName;
            user.Email = email;
            return Task.FromResult(UpdateProfileResult.Success);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<User>> ListStudentsAsync(string? search = null, string? groupIdFilter = null)
    {
        lock (lockObject.SyncRoot)
        {
            var tokens = Tokenize(search);
            var groupFilter = ParseGroupFilter(groupIdFilter);

            return Task.FromResult<IReadOnlyList<User>>(_users.Values
                .Where(user => user.Role == UserRoles.Student)
                .Where(user => MatchesGroupFilter(user, groupFilter))
                .Where(user => MatchesSearch(user, tokens))
                .Select(Clone)
                .ToList());
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<User>> ListByGroupAsync(Guid groupId)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult<IReadOnlyList<User>>(_users.Values
                .Where(user => user.Role == UserRoles.Student && user.GroupId == groupId)
                .Select(Clone)
                .ToList());
        }
    }

    /// <inheritdoc/>
    public Task<int> CountByGroupAsync(Guid groupId)
    {
        lock (lockObject.SyncRoot)
        {
            return Task.FromResult(_users.Values.Count(
                user => user.Role == UserRoles.Student && user.GroupId == groupId));
        }
    }

    /// <summary>
    /// Режим фильтра группы (FR-020): без фильтра, только без группы («none»),
    /// конкретная группа по uuid; нечитаемое значение — пустая выборка.
    /// </summary>
    private enum GroupFilterMode
    {
        None,
        Ungrouped,
        ById,
        Empty,
    }

    private readonly record struct GroupFilter(GroupFilterMode Mode, Guid GroupId);

    /// <summary>Токены поиска (FR-020): трим, разбиение по пробельным; пустой запрос — без токенов.</summary>
    private static string[] Tokenize(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? []
            : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static GroupFilter ParseGroupFilter(string? groupIdFilter)
    {
        if (string.IsNullOrWhiteSpace(groupIdFilter))
        {
            return new GroupFilter(GroupFilterMode.None, Guid.Empty);
        }

        var trimmed = groupIdFilter.Trim();

        if (trimmed.Equals("none", StringComparison.Ordinal))
        {
            return new GroupFilter(GroupFilterMode.Ungrouped, Guid.Empty);
        }

        return Guid.TryParse(trimmed, out var groupId)
            ? new GroupFilter(GroupFilterMode.ById, groupId)
            : new GroupFilter(GroupFilterMode.Empty, Guid.Empty);
    }

    private static bool MatchesGroupFilter(User user, GroupFilter filter) => filter.Mode switch
    {
        GroupFilterMode.None => true,
        GroupFilterMode.Ungrouped => user.GroupId is null,
        GroupFilterMode.ById => user.GroupId == filter.GroupId,
        GroupFilterMode.Empty => false,
        _ => false,
    };

    /// <summary>
    /// Многословный одно-полевой ci-поиск (FR-020): все токены — ci-подстроки одного
    /// и того же поля (FullName ИЛИ Login ИЛИ Email); порядок токенов не значим.
    /// </summary>
    private static bool MatchesSearch(User user, string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return true;
        }

        return MatchesAllTokens(user.FullName, tokens)
            || MatchesAllTokens(user.Login, tokens)
            || MatchesAllTokens(user.Email, tokens);
    }

    private static bool MatchesAllTokens(string field, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (!Collation.Contains(field, token))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Каскад удаления группы: сбрасывает GroupId в null всем студентам группы.
    /// ВЫЗЫВАЕТСЯ ТОЛЬКО из <see cref="InMemoryGroupRepository.DeleteAsync"/> под уже
    /// удерживаемой блокировкой хранилища — отдельную блокировку не берёт.
    /// </summary>
    internal void ClearGroupMembership(Guid groupId)
    {
        foreach (var user in _users.Values)
        {
            if (user.GroupId == groupId)
            {
                user.GroupId = null;
            }
        }
    }

    private static User Clone(User user) => new()
    {
        Id = user.Id,
        Login = user.Login,
        Email = user.Email,
        PasswordHash = user.PasswordHash,
        FullName = user.FullName,
        Role = user.Role,
        GroupId = user.GroupId,
        CreatedAt = user.CreatedAt,
    };
}
