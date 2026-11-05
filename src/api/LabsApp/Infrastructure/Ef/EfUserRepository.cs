using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LabsApp.Infrastructure;

/// <summary>
/// EF Core (PostgreSQL) реализация <see cref="IUserRepository"/>: схема —
/// src/api/db/02_schema.sql; ci-уникальность login/email — функциональные
/// уникальные индексы lower(login)/lower(email), конфликты транслируются из
/// PostgresException 23505 в <see cref="StorageConflictException"/> с теми же
/// текстами, что и in-memory реализация. Узкие мутации выполняются точечными
/// UPDATE (ExecuteUpdate) — перезаписи полного снимка нет (CR-001/SEC-001).
/// </summary>
public sealed class EfUserRepository(LabsAppDbContext db) : IUserRepository
{
    public async Task AddAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(user).State = EntityState.Detached;
            throw await ConflictForAsync(user.Login, user.Email);
        }
    }

    public async Task<User?> GetByIdAsync(Guid id) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

    public async Task<User?> GetByLoginAsync(string login) =>
        await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Login.ToLower() == login.ToLower());

    public async Task<User?> GetByEmailAsync(string email) =>
        await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

    public async Task UpdateAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var exists = await db.Users.AnyAsync(u => u.Id == user.Id);
        if (!exists)
        {
            throw new InvalidOperationException("Пользователь не найден.");
        }

        db.Users.Update(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(user).State = EntityState.Detached;
            throw await ConflictForAsync(user.Login, user.Email);
        }
    }

    public async Task<SetGroupResult> SetGroupAsync(Guid userId, Guid? groupId, Func<Guid, Task<bool>>? groupExists)
    {
        if (groupId is not null && groupExists is null)
        {
            throw new ArgumentNullException(nameof(groupExists));
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.Role != UserRoles.Student)
        {
            return SetGroupResult.StudentNotFound;
        }

        if (groupId is { } target && !await groupExists!(target))
        {
            return SetGroupResult.GroupNotFound;
        }

        try
        {
            var rows = await db.Users
                .Where(u => u.Id == userId && u.Role == UserRoles.Student)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.GroupId, groupId));
            return rows > 0 ? SetGroupResult.Success : SetGroupResult.StudentNotFound;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Группа исчезла между проверкой и записью — FK не оставляет висячего GroupId.
            return SetGroupResult.GroupNotFound;
        }
    }

    public async Task<UpdateProfileResult> UpdateProfileAsync(Guid userId, string fullName, string email)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(email);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return UpdateProfileResult.UserNotFound;
        }

        // ci-занятость email ДРУГИМ пользователем (совпадение с самим собой — не конфликт).
        var emailKey = Collation.Key(email);
        var taken = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id != userId && u.Email.ToLower() == emailKey);
        if (taken)
        {
            return UpdateProfileResult.EmailConflict;
        }

        try
        {
            var rows = await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.FullName, fullName)
                    .SetProperty(u => u.Email, email));
            return rows > 0 ? UpdateProfileResult.Success : UpdateProfileResult.UserNotFound;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Гонка двух UpdateProfile на один email — функциональный индекс пропустил
            // ровно одного (IF-015 EMAIL_CONFLICT).
            return UpdateProfileResult.EmailConflict;
        }
    }

    public async Task<IReadOnlyList<User>> ListStudentsAsync(string? search = null, string? groupIdFilter = null)
    {
        var query = db.Users.AsNoTracking().Where(u => u.Role == UserRoles.Student);

        var mode = ParseGroupFilter(groupIdFilter);
        query = mode.Mode switch
        {
            GroupFilterMode.None => query,
            GroupFilterMode.Ungrouped => query.Where(u => u.GroupId == null),
            GroupFilterMode.ById => query.Where(u => u.GroupId == mode.GroupId),
            _ => query.Where(u => false),
        };

        var candidates = await query.ToListAsync();

        // Многословный одно-полевой ci-поиск (FR-020) — в памяти по правилу
        // Collation: ВСЕ токены обязаны быть подстроками ОДНОГО И ТОГО ЖЕ поля;
        // SQL-конъюнкция «поле ИЛИ поле» на каждый токен такую семантику не
        // выражает. Выборки студентов малы — фильтрация после выборки точна и дёшева.
        var tokens = Tokenize(search);
        return tokens.Length == 0
            ? candidates
            : candidates.Where(u => MatchesSearch(u, tokens)).ToList();
    }

    public async Task<IReadOnlyList<User>> ListByGroupAsync(Guid groupId) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRoles.Student && u.GroupId == groupId)
            .ToListAsync();

    public async Task<int> CountByGroupAsync(Guid groupId) =>
        await db.Users.AsNoTracking()
            .CountAsync(u => u.Role == UserRoles.Student && u.GroupId == groupId);

    /// <summary>Тексты конфликтов ci-уникальности — идентичны in-memory реализации.</summary>
    private async Task<StorageConflictException> ConflictForAsync(string login, string email)
    {
        var loginOwner = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Login.ToLower() == login.ToLower());
        return loginOwner
            ? new StorageConflictException("Пользователь с таким логином уже существует.")
            : new StorageConflictException("Пользователь с таким email уже существует.");
    }

    private enum GroupFilterMode
    {
        None,
        Ungrouped,
        ById,
        Empty,
    }

    private readonly record struct GroupFilter(GroupFilterMode Mode, Guid GroupId);

    private static string[] Tokenize(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? []
            : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Все токены — ci-подстроки одного и того же поля (зеркало in-memory).</summary>
    private static bool MatchesSearch(User user, string[] tokens) =>
        MatchesAllTokens(user.FullName, tokens)
        || MatchesAllTokens(user.Login, tokens)
        || MatchesAllTokens(user.Email, tokens);

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
}
