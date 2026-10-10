using LabsApp.Auth.RateLimiting;
using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Тестовые (фиктивные, потокобезопасные) реализации интерфейсов персистенции
/// FR-024/IF-015 кейса TS-134 «Репозитории: подмена реализаций не требует правок
/// контроллеров»: IUserRepository, IGroupRepository, ILabRepository,
/// ISubmissionRepository, ISecurityTokenRepository, IRateLimitStore. Ничего не
/// знают о InMemory-реализациях приложения — независимый код с собственными
/// словарями и собственными локами; это доказывает контракт FR-024: контроллеры
/// и прикладные сервисы зависят ТОЛЬКО от интерфейсов. Семантика повторяет
/// контракты интерфейсов (ci-правила Collation, атомарные проверки+вставки с
/// StorageConflictException, ленивая живость токенов по TimeProvider) в объёме,
/// достаточном для сквозного прогона регистрация → вход → GET /labs; инспекционные
/// методы помечены явно.
/// </summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, User> _byId = [];
    private readonly Dictionary<string, Guid> _byLoginCi = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _byEmailCi = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Add(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (_gate)
        {
            var loginKey = Collation.Key(user.Login);
            var emailKey = Collation.Key(user.Email);
            if (_byLoginCi.ContainsKey(loginKey))
            {
                throw new StorageConflictException("FakeUserRepository: ci-логин занят.");
            }

            if (_byEmailCi.ContainsKey(emailKey))
            {
                throw new StorageConflictException("FakeUserRepository: ci-email занят.");
            }

            var stored = Clone(user);
            _byId[stored.Id] = stored;
            _byLoginCi[loginKey] = stored.Id;
            _byEmailCi[emailKey] = stored.Id;
        }
    }

    /// <inheritdoc/>
    public User? GetById(Guid id)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(id, out var found) ? Clone(found) : null;
        }
    }

    /// <inheritdoc/>
    public User? GetByLogin(string login)
    {
        lock (_gate)
        {
            return _byLoginCi.TryGetValue(Collation.Key(login), out var id) ? Clone(_byId[id]) : null;
        }
    }

    /// <inheritdoc/>
    public User? GetByEmail(string email)
    {
        lock (_gate)
        {
            return _byEmailCi.TryGetValue(Collation.Key(email), out var id) ? Clone(_byId[id]) : null;
        }
    }

    /// <inheritdoc/>
    public void Update(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (_gate)
        {
            if (!_byId.TryGetValue(user.Id, out var existing))
            {
                throw new InvalidOperationException("FakeUserRepository: запись не существует.");
            }

            var loginKey = Collation.Key(user.Login);
            var emailKey = Collation.Key(user.Email);
            if (_byLoginCi.TryGetValue(loginKey, out var loginOwner) && loginOwner != user.Id)
            {
                throw new StorageConflictException("FakeUserRepository: ci-логин занят другим.");
            }

            if (_byEmailCi.TryGetValue(emailKey, out var emailOwner) && emailOwner != user.Id)
            {
                throw new StorageConflictException("FakeUserRepository: ci-email занят другим.");
            }

            _byLoginCi.Remove(Collation.Key(existing.Login));
            _byEmailCi.Remove(Collation.Key(existing.Email));
            var stored = Clone(user);
            _byId[stored.Id] = stored;
            _byLoginCi[loginKey] = stored.Id;
            _byEmailCi[emailKey] = stored.Id;
        }
    }

    /// <inheritdoc/>
    public SetGroupResult SetGroup(Guid userId, Guid? groupId, Func<Guid, bool>? groupExists)
    {
        if (groupId is not null && groupExists is null)
        {
            throw new ArgumentNullException(nameof(groupExists));
        }

        lock (_gate)
        {
            // Узкая мутация (SEC-001): проверка существования/роли, вызов делегата
            // и запись — в одной критической секции фиктивного хранилища; меняется
            // ТОЛЬКО GroupId хранимой записи (семантика зеркальна InMemoryUserRepository).
            if (!_byId.TryGetValue(userId, out var user) || user.Role != UserRoles.Student)
            {
                return SetGroupResult.StudentNotFound;
            }

            if (groupId is { } target && !groupExists!(target))
            {
                return SetGroupResult.GroupNotFound;
            }

            user.GroupId = groupId;
            return SetGroupResult.Success;
        }
    }

    /// <inheritdoc/>
    public bool SetPassword(Guid userId, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        if (passwordHash.Length == 0)
        {
            throw new ArgumentNullException(nameof(passwordHash));
        }

        lock (_gate)
        {
            // Узкая мутация (CR-001/SEC-001): меняется ТОЛЬКО PasswordHash хранимой
            // записи; ci-индексы и прочие поля не затрагиваются (зеркально
            // InMemoryUserRepository).
            if (!_byId.TryGetValue(userId, out var user))
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

        lock (_gate)
        {
            // Узкая мутация (CR-001/SEC-001): ci-занятость email ДРУГИМ пользователем
            // — конфликт; меняются ТОЛЬКО FullName и Email с перепривязкой ci-email
            // (зеркально InMemoryUserRepository).
            if (!_byId.TryGetValue(userId, out var user))
            {
                return UpdateProfileResult.UserNotFound;
            }

            var newEmailKey = Collation.Key(email);
            if (_byEmailCi.TryGetValue(newEmailKey, out var owner) && owner != userId)
            {
                return UpdateProfileResult.EmailConflict;
            }

            var oldEmailKey = Collation.Key(user.Email);
            if (!oldEmailKey.Equals(newEmailKey, StringComparison.Ordinal))
            {
                _byEmailCi.Remove(oldEmailKey);
                _byEmailCi[newEmailKey] = userId;
            }

            user.FullName = fullName;
            user.Email = email;
            return UpdateProfileResult.Success;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<User> ListStudents(string? search = null, string? groupIdFilter = null)
    {
        lock (_gate)
        {
            IEnumerable<User> students = _byId.Values.Where(user => user.Role == UserRoles.Student);

            if (!string.IsNullOrEmpty(groupIdFilter))
            {
                students = groupIdFilter.Trim().ToLowerInvariant() switch
                {
                    "none" => students.Where(user => user.GroupId is null),
                    var raw when Guid.TryParse(raw, out var groupId) =>
                        students.Where(user => user.GroupId == groupId),
                    _ => Enumerable.Empty<User>(),
                };
            }

            return students
                .Where(user => MatchesSearch(user, search))
                .Select(Clone)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<User> ListByGroup(Guid groupId)
    {
        lock (_gate)
        {
            return _byId.Values
                .Where(user => user.Role == UserRoles.Student && user.GroupId == groupId)
                .Select(Clone)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public int CountByGroup(Guid groupId)
    {
        lock (_gate)
        {
            return _byId.Values.Count(user => user.Role == UserRoles.Student && user.GroupId == groupId);
        }
    }

    private static bool MatchesSearch(User user, string? search)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(token => Collation.Contains(user.FullName, token))
            || tokens.All(token => Collation.Contains(user.Login, token))
            || tokens.All(token => Collation.Contains(user.Email, token));
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

/// <summary>Фиктивный IGroupRepository (TS-134): ci-уникальность Name, каскад удаления — сброс GroupId студентов.</summary>
public sealed class FakeGroupRepository(IUserRepository users) : IGroupRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Group> _byId = [];
    private readonly Dictionary<string, Guid> _byNameCi = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Add(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        lock (_gate)
        {
            var nameKey = Collation.Key(group.Name);
            if (_byNameCi.ContainsKey(nameKey))
            {
                throw new StorageConflictException("FakeGroupRepository: ci-имя занято.");
            }

            _byId[group.Id] = group;
            _byNameCi[nameKey] = group.Id;
        }
    }

    /// <inheritdoc/>
    public Group? GetById(Guid id)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(id, out var found) ? found : null;
        }
    }

    /// <inheritdoc/>
    public Group? GetByName(string name)
    {
        lock (_gate)
        {
            return _byNameCi.TryGetValue(Collation.Key(name), out var id) ? _byId[id] : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Group> GetAll()
    {
        lock (_gate)
        {
            return _byId.Values.ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Group> List() => GetAll();

    /// <inheritdoc/>
    public bool ExistsNameCi(string name)
    {
        lock (_gate)
        {
            return _byNameCi.ContainsKey(Collation.Key(name));
        }
    }

    /// <inheritdoc/>
    public void Update(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        lock (_gate)
        {
            if (!_byId.TryGetValue(group.Id, out var existing))
            {
                throw new InvalidOperationException("FakeGroupRepository: запись не существует.");
            }

            var nameKey = Collation.Key(group.Name);
            if (_byNameCi.TryGetValue(nameKey, out var owner) && owner != group.Id)
            {
                throw new StorageConflictException("FakeGroupRepository: ci-имя занято другой.");
            }

            _byNameCi.Remove(Collation.Key(existing.Name));
            _byId[group.Id] = group;
            _byNameCi[nameKey] = group.Id;
        }
    }

    /// <summary>
    /// Каскад ON DELETE SET NULL (domain_model): фиктивная реализация выполняет
    /// удаление группы и сброс GroupId её студентов через пользовательский
    /// репозиторий (единая критическая секция каскада кейсом TS-134 не проверяется).
    /// </summary>
    public void Delete(Guid id)
    {
        lock (_gate)
        {
            _byId.Remove(id);
            foreach (var pair in _byNameCi.Where(pair => pair.Value == id).ToList())
            {
                _byNameCi.Remove(pair.Key);
            }
        }

        foreach (var student in users.ListByGroup(id))
        {
            users.Update(new User
            {
                Id = student.Id,
                Login = student.Login,
                Email = student.Email,
                PasswordHash = student.PasswordHash,
                FullName = student.FullName,
                Role = student.Role,
                GroupId = null,
                CreatedAt = student.CreatedAt,
            });
        }
    }
}

/// <summary>Фиктивный ILabRepository (TS-134): атомарная пара (semester, number), каскад сдач через ISubmissionRepository.</summary>
public sealed class FakeLabRepository(ISubmissionRepository submissions) : ILabRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Lab> _byId = [];
    private readonly Dictionary<(int Semester, int Number), Guid> _byPair = [];

    /// <inheritdoc/>
    public void Add(Lab lab)
    {
        ArgumentNullException.ThrowIfNull(lab);

        lock (_gate)
        {
            var pair = (lab.Semester, lab.Number);
            if (_byPair.ContainsKey(pair))
            {
                throw new StorageConflictException("FakeLabRepository: пара (semester, number) занята.");
            }

            _byId[lab.Id] = lab;
            _byPair[pair] = lab.Id;
        }
    }

    /// <inheritdoc/>
    public Lab? GetById(Guid id)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(id, out var found) ? found : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Lab> GetAll()
    {
        lock (_gate)
        {
            return _byId.Values.ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Lab> ListByFilter(int? semester)
    {
        lock (_gate)
        {
            return _byId.Values
                .Where(lab => semester is null || lab.Semester == semester.Value)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<int> Semesters()
    {
        lock (_gate)
        {
            return _byId.Values.Select(lab => lab.Semester).Distinct().OrderBy(semester => semester).ToList();
        }
    }

    /// <inheritdoc/>
    public Lab? TryGetByPair(int semester, int number)
    {
        lock (_gate)
        {
            return _byPair.TryGetValue((semester, number), out var id) ? _byId[id] : null;
        }
    }

    /// <inheritdoc/>
    public bool ExistsPair(int semester, int number, Guid? exceptId = null)
    {
        lock (_gate)
        {
            return _byPair.TryGetValue((semester, number), out var id) && id != exceptId;
        }
    }

    /// <inheritdoc/>
    public void Update(Lab lab)
    {
        ArgumentNullException.ThrowIfNull(lab);

        lock (_gate)
        {
            if (!_byId.TryGetValue(lab.Id, out var existing))
            {
                throw new InvalidOperationException("FakeLabRepository: запись не существует.");
            }

            var pair = (lab.Semester, lab.Number);
            if (_byPair.TryGetValue(pair, out var owner) && owner != lab.Id)
            {
                throw new StorageConflictException("FakeLabRepository: пара занята другой записью.");
            }

            _byPair.Remove((existing.Semester, existing.Number));
            _byId[lab.Id] = lab;
            _byPair[pair] = lab.Id;
        }
    }

    /// <inheritdoc/>
    public void Delete(Guid id)
    {
        lock (_gate)
        {
            if (!_byId.Remove(id, out var existing))
            {
                return;
            }

            _byPair.Remove((existing.Semester, existing.Number));
        }

        submissions.DeleteByLabId(id);
    }
}

/// <summary>Фиктивный ISubmissionRepository (TS-134): атомарный upsert пары (studentId, labId), обе null-даты сохраняются.</summary>
public sealed class FakeSubmissionRepository : ISubmissionRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Submission> _byId = [];
    private readonly Dictionary<(Guid StudentId, Guid LabId), Guid> _byPair = [];

    /// <inheritdoc/>
    public Submission? Upsert(
        Guid studentId,
        Guid labId,
        DateOnly? submitDate,
        DateOnly? defenseDate,
        Guid? updatedBy,
        DateTime updatedAt)
    {
        lock (_gate)
        {
            if (_byPair.TryGetValue((studentId, labId), out var existingId))
            {
                var existing = _byId[existingId];
                existing.SubmitDate = submitDate;
                existing.DefenseDate = defenseDate;
                existing.UpdatedBy = updatedBy;
                existing.UpdatedAt = updatedAt;
                return existing;
            }

            var created = new Submission
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
                LabId = labId,
                SubmitDate = submitDate,
                DefenseDate = defenseDate,
                UpdatedBy = updatedBy,
                UpdatedAt = updatedAt,
            };
            _byId[created.Id] = created;
            _byPair[(studentId, labId)] = created.Id;
            return created;
        }
    }

    /// <inheritdoc/>
    public Submission? GetByStudentAndLab(Guid studentId, Guid labId)
    {
        lock (_gate)
        {
            return _byPair.TryGetValue((studentId, labId), out var id) ? _byId[id] : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> ListByLabIds(IReadOnlyCollection<Guid> labIds)
    {
        lock (_gate)
        {
            return _byId.Values.Where(submission => labIds.Contains(submission.LabId)).ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> ListByStudent(Guid studentId)
    {
        lock (_gate)
        {
            return _byId.Values.Where(submission => submission.StudentId == studentId).ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> GetByPairs(IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<Guid> labIds)
    {
        lock (_gate)
        {
            return studentIds
                .SelectMany(studentId => labIds.Select(labId => (studentId, labId)))
                .Where(pair => _byPair.ContainsKey(pair))
                .Select(pair => _byId[_byPair[pair]])
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> ListForStudentAndSemester(Guid studentId, IReadOnlyCollection<Guid> semesterLabIds)
    {
        lock (_gate)
        {
            return _byId.Values
                .Where(submission => submission.StudentId == studentId && semesterLabIds.Contains(submission.LabId))
                .ToList();
        }
    }

    /// <inheritdoc/>
    public void DeleteByLabId(Guid labId)
    {
        lock (_gate)
        {
            foreach (var id in _byId.Values
                .Where(submission => submission.LabId == labId)
                .Select(submission => submission.Id)
                .ToList())
            {
                var removed = _byId[id];
                _byId.Remove(id);
                _byPair.Remove((removed.StudentId, removed.LabId));
            }
        }
    }
}

/// <summary>
/// Фиктивный ISecurityTokenRepository (TS-134): хранит только хэши; живость
/// (refresh — revokedAt == null && expiresAt &gt; now; код/reset — usedAt == null
/// && expiresAt &gt; now) вычисляет лениво по TimeProvider (IF-015).
/// </summary>
public sealed class FakeSecurityTokenRepository(TimeProvider timeProvider) : ISecurityTokenRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RefreshToken> _refreshByHash = new(StringComparer.Ordinal);
    private readonly List<RecoveryCode> _recoveryCodes = [];
    private readonly Dictionary<string, PasswordResetToken> _resetByHash = new(StringComparer.Ordinal);

    /// <summary>Инспекция тестом: снимок всех refresh-записей (TS-134 проверяет, что запись легла в подмену).</summary>
    public IReadOnlyList<RefreshToken> RefreshTokensSnapshot()
    {
        lock (_gate)
        {
            return _refreshByHash.Values.ToList();
        }
    }

    /// <inheritdoc/>
    public void Add(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        lock (_gate)
        {
            if (!_refreshByHash.TryAdd(token.TokenHash, token))
            {
                throw new InvalidOperationException("FakeSecurityTokenRepository: повторный TokenHash.");
            }
        }
    }

    /// <inheritdoc/>
    public RefreshToken? FindLiveByHash(string tokenHash)
    {
        lock (_gate)
        {
            return _refreshByHash.TryGetValue(tokenHash, out var found) && IsLiveRefresh(found) ? found : null;
        }
    }

    /// <inheritdoc/>
    public void Revoke(Guid id)
    {
        lock (_gate)
        {
            var found = _refreshByHash.Values.FirstOrDefault(token => token.Id == id);
            if (found is not null && found.RevokedAt is null)
            {
                found.RevokedAt = Now;
            }
        }
    }

    /// <inheritdoc/>
    public void RevokeAllForUserExcept(Guid userId, string? exceptTokenHash)
    {
        lock (_gate)
        {
            foreach (var token in _refreshByHash.Values)
            {
                if (token.UserId == userId && token.RevokedAt is null && token.TokenHash != exceptTokenHash)
                {
                    token.RevokedAt = Now;
                }
            }
        }
    }

    /// <inheritdoc/>
    public void AddLive(RecoveryCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        lock (_gate)
        {
            foreach (var existing in _recoveryCodes)
            {
                if (existing.UserId == code.UserId && IsLiveCode(existing))
                {
                    existing.UsedAt = Now;
                }
            }

            _recoveryCodes.Add(code);
        }
    }

    /// <inheritdoc/>
    public RecoveryCode? FindLiveForUser(Guid userId)
    {
        lock (_gate)
        {
            RecoveryCode? latest = null;
            foreach (var code in _recoveryCodes)
            {
                if (code.UserId == userId && IsLiveCode(code))
                {
                    latest = code;
                }
            }

            return latest;
        }
    }

    /// <inheritdoc/>
    public void IncrementAttemptsOnLive(Guid codeId)
    {
        lock (_gate)
        {
            var found = _recoveryCodes.FirstOrDefault(code => code.Id == codeId);
            if (found is null || !IsLiveCode(found))
            {
                return;
            }

            found.Attempts++;
            if (found.Attempts >= 5)
            {
                found.UsedAt = Now;
            }
        }
    }

    /// <inheritdoc/>
    public void MarkUsed(Guid codeId)
    {
        lock (_gate)
        {
            var found = _recoveryCodes.FirstOrDefault(code => code.Id == codeId);
            if (found is not null && found.UsedAt is null)
            {
                found.UsedAt = Now;
            }
        }
    }

    /// <inheritdoc/>
    public void Add(PasswordResetToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        lock (_gate)
        {
            if (!_resetByHash.TryAdd(token.TokenHash, token))
            {
                throw new InvalidOperationException("FakeSecurityTokenRepository: повторный reset-TokenHash.");
            }
        }
    }

    /// <inheritdoc/>
    public PasswordResetToken? FindLiveResetByHash(string resetTokenHash)
    {
        lock (_gate)
        {
            return _resetByHash.TryGetValue(resetTokenHash, out var found) && IsLiveReset(found) ? found : null;
        }
    }

    /// <inheritdoc/>
    public void MarkUsed(string resetTokenHash)
    {
        lock (_gate)
        {
            if (_resetByHash.TryGetValue(resetTokenHash, out var found) && found.UsedAt is null)
            {
                found.UsedAt = Now;
            }
        }
    }

    /// <inheritdoc/>
    public void ConsumeAllForUser(Guid userId)
    {
        lock (_gate)
        {
            foreach (var token in _resetByHash.Values)
            {
                if (token.UserId == userId && token.UsedAt is null)
                {
                    token.UsedAt = Now;
                }
            }
        }
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private bool IsLiveRefresh(RefreshToken token) => token.RevokedAt is null && token.ExpiresAt > Now;

    private bool IsLiveCode(RecoveryCode code) => code.UsedAt is null && code.ExpiresAt > Now;

    private bool IsLiveReset(PasswordResetToken token) => token.UsedAt is null && token.ExpiresAt > Now;
}

/// <summary>
/// Фиктивный IRateLimitStore (TS-134): «политика → ключ → метки окна» под общим
/// локом; каждая операция атомарна (составные последовательности сериализует
/// движок лимитера своим локом — контракт IF-006/IF-015).
/// </summary>
public sealed class FakeRateLimitStore : IRateLimitStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, List<long>>> _policies = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public int TrackedKeysCount(string policy)
    {
        lock (_gate)
        {
            return _policies.TryGetValue(policy, out var keys) ? keys.Count : 0;
        }
    }

    /// <inheritdoc/>
    public bool TryGetMarks(string policy, string key, out List<long> marks)
    {
        lock (_gate)
        {
            if (_policies.TryGetValue(policy, out var keys) && keys.TryGetValue(key, out var found))
            {
                marks = found;
                return true;
            }
        }

        marks = [];
        return false;
    }

    /// <inheritdoc/>
    public List<long> GetOrAddMarks(string policy, string key)
    {
        lock (_gate)
        {
            if (!_policies.TryGetValue(policy, out var keys))
            {
                _policies[policy] = keys = [];
            }

            if (!keys.TryGetValue(key, out var marks))
            {
                keys[key] = marks = [];
            }

            return marks;
        }
    }

    /// <inheritdoc/>
    public bool Remove(string policy, string key)
    {
        lock (_gate)
        {
            return _policies.TryGetValue(policy, out var keys) && keys.Remove(key);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetKeys(string policy)
    {
        lock (_gate)
        {
            return _policies.TryGetValue(policy, out var keys) ? keys.Keys.ToList() : [];
        }
    }
}
