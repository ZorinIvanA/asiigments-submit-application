using System.Globalization;
using LabsApp.Domain.Entities;

namespace LabsApp.Storage.InMemory;

/// <summary>
/// In-memory реализация <see cref="ILabRepository"/> (FR-002): Dictionary + индекс пары
/// «{semester}:{number}» (InvariantCulture), единая блокировка хранилища (ADR-002),
/// метки времени — TimeProvider (ADR-002/FR-003: единственный источник бизнес-времени).
/// Delete выполняет каскадное удаление сдач работы в той же критической секции.
/// Хранилище хранит и отдаёт КОПИИ-снимки сущностей.
/// </summary>
public sealed class InMemoryLabRepository(
    StorageLock lockObject,
    InMemorySubmissionRepository submissions,
    TimeProvider timeProvider) : ILabRepository
{
    private readonly Dictionary<Guid, Lab> _labs = new();
    private readonly Dictionary<string, Guid> _byPair = new(StringComparer.Ordinal);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    /// <inheritdoc/>
    public void Add(Lab lab)
    {
        ArgumentNullException.ThrowIfNull(lab);

        lock (lockObject.SyncRoot)
        {
            var pairKey = PairKey(lab.Semester, lab.Number);

            if (_byPair.ContainsKey(pairKey))
            {
                throw new StorageConflictException("Лабораторная с таким номером уже есть в семестре.");
            }

            _labs[lab.Id] = Clone(lab);
            _byPair[pairKey] = lab.Id;
        }
    }

    /// <inheritdoc/>
    public Lab? GetById(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            return _labs.TryGetValue(id, out var lab) ? Clone(lab) : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Lab> GetAll()
    {
        lock (lockObject.SyncRoot)
        {
            return _labs.Values.Select(Clone).ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Lab> ListByFilter(int? semester)
    {
        lock (lockObject.SyncRoot)
        {
            return semester is null
                ? _labs.Values.Select(Clone).ToList()
                : _labs.Values
                    .Where(lab => lab.Semester == semester.Value)
                    .Select(Clone)
                    .ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<int> Semesters()
    {
        lock (lockObject.SyncRoot)
        {
            return _labs.Values
                .Select(lab => lab.Semester)
                .Distinct()
                .OrderBy(semester => semester)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public Lab? TryGetByPair(int semester, int number)
    {
        lock (lockObject.SyncRoot)
        {
            return _byPair.TryGetValue(PairKey(semester, number), out var id)
                ? Clone(_labs[id])
                : null;
        }
    }

    /// <inheritdoc/>
    public bool ExistsPair(int semester, int number, Guid? exceptId = null)
    {
        lock (lockObject.SyncRoot)
        {
            return _byPair.TryGetValue(PairKey(semester, number), out var id)
                && id != exceptId;
        }
    }

    /// <inheritdoc/>
    public void Update(Lab lab)
    {
        ArgumentNullException.ThrowIfNull(lab);

        lock (lockObject.SyncRoot)
        {
            if (!_labs.TryGetValue(lab.Id, out var existing))
            {
                throw new InvalidOperationException("Лабораторная работа не найдена.");
            }

            var pairKey = PairKey(lab.Semester, lab.Number);

            if (_byPair.TryGetValue(pairKey, out var pairOwner) && pairOwner != lab.Id)
            {
                throw new StorageConflictException("Лабораторная с таким номером уже есть в семестре.");
            }

            _byPair.Remove(PairKey(existing.Semester, existing.Number));
            lab.UpdatedAt = Now();
            _labs[lab.Id] = Clone(lab);
            _byPair[pairKey] = lab.Id;
        }
    }

    /// <inheritdoc/>
    public void Delete(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            if (!_labs.Remove(id, out var lab))
            {
                return;
            }

            _byPair.Remove(PairKey(lab.Semester, lab.Number));
            submissions.DeleteByLabIdCore(id);
        }
    }

    private static string PairKey(int semester, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{semester}:{number}");

    private static Lab Clone(Lab lab) => new()
    {
        Id = lab.Id,
        Semester = lab.Semester,
        Number = lab.Number,
        Content = lab.Content,
        AssignmentUrl = lab.AssignmentUrl,
        DefenseRequired = lab.DefenseRequired,
        CreatedAt = lab.CreatedAt,
        UpdatedAt = lab.UpdatedAt,
    };
}
