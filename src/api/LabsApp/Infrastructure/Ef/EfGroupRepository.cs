using LabsApp.Domain;
using LabsApp.Domain.Entities;
using LabsApp.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LabsApp.Infrastructure;

/// <summary>
/// EF Core (PostgreSQL) реализация <see cref="IGroupRepository"/>. Каскад
/// удаления группы (сброс GroupId студентам) выполняет FK базы —
/// ON DELETE SET NULL (src/api/db/02_schema.sql).
/// </summary>
public sealed class EfGroupRepository(LabsAppDbContext db) : IGroupRepository
{
    public async Task AddAsync(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        db.Groups.Add(group);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(group).State = EntityState.Detached;
            throw new StorageConflictException("Группа с таким названием уже существует.");
        }
    }

    public async Task<Group?> GetByIdAsync(Guid id) =>
        await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);

    public async Task<Group?> GetByNameAsync(string name) =>
        await db.Groups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Name.ToLower() == name.ToLower());

    public async Task<IReadOnlyList<Group>> GetAllAsync() =>
        await db.Groups.AsNoTracking().ToListAsync();

    public async Task<IReadOnlyList<Group>> ListAsync() => await GetAllAsync();

    public async Task<bool> ExistsNameCiAsync(string name) =>
        await db.Groups.AsNoTracking().AnyAsync(g => g.Name.ToLower() == name.ToLower());

    public async Task UpdateAsync(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var exists = await db.Groups.AnyAsync(g => g.Id == group.Id);
        if (!exists)
        {
            throw new InvalidOperationException("Группа не найдена.");
        }

        db.Groups.Update(group);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(group).State = EntityState.Detached;
            throw new StorageConflictException("Группа с таким названием уже существует.");
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        // Идемпотентно; ON DELETE SET NULL сбрасывает GroupId студентов.
        await db.Groups.Where(g => g.Id == id).ExecuteDeleteAsync();
    }
}
