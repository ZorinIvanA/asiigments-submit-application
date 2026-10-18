using LabsApp.Domain;
using LabsApp.Domain.Entities;

namespace LabsApp.Storage.InMemory;

/// <summary>
/// In-memory реализация <see cref="IUserRepository"/> (FR-024): Dictionary + индексы
/// ci-уникальности login/email (ключи <see cref="Collation.Key"/>, сравнение Ordinal),
/// составные операции атомарны под общим <see cref="StorageLock"/> (IF-015). Каждая составная операция — целиком в
/// критической секции. Хранилище хранит и отдаёт КОПИИ-снимки сущностей: внешняя
/// мутация выданной копии не влияет на хранилище и ci-индексы, изменения — только
/// через узкие атомарные мутаторы <see cref="SetGroup"/> (группа, SEC-001),
/// <see cref="SetPassword"/> (пароль) и <see cref="UpdateProfile"/> (профиль) —
/// каждый меняет ТОЛЬКО свои поля записи и не откатывает конкурентные мутации
/// соседних полей (CR-001); <see cref="Update"/> — полная замена, прод-контроллерами
/// не применяется (документально суженный контракт, OQ-002).
/// </summary>
public sealed class InMemoryUserRepository(StorageLock lockObject) : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();
    private readonly Dictionary<string, Guid> _byLogin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _byEmail = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Add(User user)
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
    }

    /// <inheritdoc/>
    public User? GetById(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            return _users.TryGetValue(id, out var user) ? Clone(user) : null;
        }
    }

    /// <inheritdoc/>
    public User? GetByLogin(string login)
    {
        lock (lockObject.SyncRoot)
        {
            return _byLogin.TryGetValue(Collation.Key(login), out var id)
                ? Clone(_users[id])
                : null;
        }
    }

    /// <inheritdoc/>
    public User? GetByEmail(string email)
    {
        lock (lockObject.SyncRoot)
        {
            return _byEmail.TryGetValue(Collation.Key(email), out var id)
                ? Clone(_users[id])
                : null;
        }
    }

    /// <inheritdoc/>
    public void Update(User user)
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
    }

    /// <inheritdoc/>
    public SetGroupResult SetGroup(Guid userId, Guid? groupId, Func<Guid, bool>? groupExists)
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
            // после проверки, не оставляет висячего GroupId. Делегат вызывается
            // под уже удерживаемой блокировкой: замок хранилища единый (ADR-002),
            // Monitor реентерабелен.
            if (!_users.TryGetValue(userId, out var user) || user.Role != UserRoles.Student)
            {
                return SetGroupResult.StudentNotFound;
            }

            if (groupId is { } target)
            {
                // groupExists обязателен для ненулевой цели — guard в начале метода.
                if (!groupExists!(target))
                {
                    return SetGroupResult.GroupNotFound;
                }
            }

            user.GroupId = groupId;
            return SetGroupResult.Success;
        }
    }

    /// <inheritdoc/>
    public bool SetPassword(Guid userId, string passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            throw new ArgumentNullException(nameof(passwordHash));
        }

        lock (lockObject.SyncRoot)
        {
            // Узкая мутация (CR-001/SEC-001): проверка существования и запись — одна
            // критическая секция, меняется ТОЛЬКО PasswordHash хранимой записи (без
            // перечитывания и перезаписи полного снимка), поэтому конкурентные правка
            // профиля и назначение группы не откатываются; ci-индексы не затрагиваются.
            if (!_users.TryGetValue(userId, out var user))
            {
                return false;
            }

            user.PasswordHash = passwordHash;
            return true;
        }
    }

    /// <inheritdoc/>
    public UpdateProfileResult UpdateProfile(Guid userId, string fullName, string email)
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
                return UpdateProfileResult.UserNotFound;
            }

            var newEmailKey = Collation.Key(email);
            if (_byEmail.TryGetValue(newEmailKey, out var owner) && owner != userId)
            {
                return UpdateProfileResult.EmailConflict;
            }

            var oldEmailKey = Collation.Key(user.Email);
            if (!oldEmailKey.Equals(newEmailKey, StringComparison.Ordinal))
            {
                _byEmail.Remove(oldEmailKey);
                _byEmail[newEmailKey] = userId;
            }

            user.FullName = fullName;
            user.Email = email;
            return UpdateProfileResult.Success;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<User> ListStudents(string? search = null, string? groupIdFilter = null)
    {
        lock (lockObject.SyncRoot)
        {
            var tokens = Tokenize(search);
            var groupFilter = ParseGroupFilter(groupIdFilter);

            return _users.Values
                .Where(user => user.Role == UserRoles.Student)
                .Where(user => MatchesGroupFilter(user, groupFilter))
                .Where(user => MatchesSearch(user, tokens))
                .Select(Clone)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<User> ListByGroup(Guid groupId)
    {
        lock (lockObject.SyncRoot)
        {
            return _users.Values
                .Where(user => user.Role == UserRoles.Student && user.GroupId == groupId)
                .Select(Clone)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public int CountByGroup(Guid groupId)
    {
        lock (lockObject.SyncRoot)
        {
            return _users.Values.Count(
                user => user.Role == UserRoles.Student && user.GroupId == groupId);
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
    /// ВЫЗЫВАЕТСЯ ТОЛЬКО из <see cref="InMemoryGroupRepository.Delete"/> под уже
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
