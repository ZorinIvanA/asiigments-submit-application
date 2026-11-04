using LabsApp.Domain.Entities;
using LabsApp.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LabsApp.Infrastructure;

/// <summary>
/// EF Core (PostgreSQL) реализация <see cref="ILabRepository"/>: уникальность
/// пары (Semester, Number) — констрейнт БД; каскад удаления сдач работы —
/// FK ON DELETE CASCADE. UpdatedAt при обновлении проставляется из TimeProvider.
/// </summary>
public sealed class EfLabRepository(LabsAppDbContext db, TimeProvider timeProvider) : ILabRepository
{
    public async Task AddAsync(Lab lab)
    {
        ArgumentNullException.ThrowIfNull(lab);

        db.Labs.Add(lab);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(lab).State = EntityState.Detached;
            throw new StorageConflictException("Лабораторная с таким номером уже есть в семестре.");
        }
    }

    public async Task<Lab?> GetByIdAsync(Guid id) =>
        await db.Labs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);

    public async Task<IReadOnlyList<Lab>> GetAllAsync() =>
        await db.Labs.AsNoTracking().ToListAsync();

    public async Task<IReadOnlyList<Lab>> ListByFilterAsync(int? semester) =>
        semester is null
            ? await db.Labs.AsNoTracking().ToListAsync()
            : await db.Labs.AsNoTracking().Where(l => l.Semester == semester.Value).ToListAsync();

    public async Task<IReadOnlyList<int>> SemestersAsync() =>
        await db.Labs.AsNoTracking()
            .Select(l => l.Semester)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();

    public async Task<Lab?> TryGetByPairAsync(int semester, int number) =>
        await db.Labs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Semester == semester && l.Number == number);

    public async Task<bool> ExistsPairAsync(int semester, int number, Guid? exceptId = null) =>
        await db.Labs.AsNoTracking()
            .AnyAsync(l => l.Semester == semester && l.Number == number && l.Id != exceptId);

    public async Task UpdateAsync(Lab lab)
    {
        ArgumentNullException.ThrowIfNull(lab);

        var exists = await db.Labs.AnyAsync(l => l.Id == lab.Id);
        if (!exists)
        {
            throw new InvalidOperationException("Лабораторная работа не найдена.");
        }

        lab.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        db.Labs.Update(lab);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(lab).State = EntityState.Detached;
            throw new StorageConflictException("Лабораторная с таким номером уже есть в семестре.");
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        // Идемпотентно; сдачи работы удаляет FK ON DELETE CASCADE.
        await db.Labs.Where(l => l.Id == id).ExecuteDeleteAsync();
    }
}
