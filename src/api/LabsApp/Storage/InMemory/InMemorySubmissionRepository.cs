using LabsApp.Domain.Entities;

namespace LabsApp.Storage.InMemory;

/// <summary>
/// In-memory реализация <see cref="ISubmissionRepository"/> (FR-024): Dictionary +
/// индекс пары (StudentId, LabId), составные операции атомарны под общим
/// <see cref="StorageLock"/> (IF-015). Upsert атомарен; семантика дат — data_design
/// (FR-021): null = сброс значения, запись с обеими null-датами сохраняется
/// (upsert пары никогда не удаляет запись). Хранилище хранит и отдаёт
/// КОПИИ-снимки сущностей.
/// </summary>
public sealed class InMemorySubmissionRepository(StorageLock lockObject) : ISubmissionRepository
{
    private readonly Dictionary<Guid, Submission> _submissions = new();
    private readonly Dictionary<(Guid StudentId, Guid LabId), Guid> _byPair = new();

    /// <inheritdoc/>
    public Submission? Upsert(
        Guid studentId,
        Guid labId,
        DateOnly? submitDate,
        DateOnly? defenseDate,
        Guid? updatedBy,
        DateTime updatedAt)
    {
        lock (lockObject.SyncRoot)
        {
            if (_byPair.TryGetValue((studentId, labId), out var id))
            {
                var existing = _submissions[id];

                // Полная замена обеих дат (data_design, FR-021): null = сброс
                // значения даты; запись пары сохраняется (Id стабилен).
                existing.SubmitDate = submitDate;
                existing.DefenseDate = defenseDate;
                existing.UpdatedBy = updatedBy;
                existing.UpdatedAt = updatedAt;
                return Clone(existing);
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
            _submissions[created.Id] = created;
            _byPair[(studentId, labId)] = created.Id;
            return Clone(created);
        }
    }

    /// <inheritdoc/>
    public Submission? GetByStudentAndLab(Guid studentId, Guid labId)
    {
        lock (lockObject.SyncRoot)
        {
            return _byPair.TryGetValue((studentId, labId), out var id)
                ? Clone(_submissions[id])
                : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> ListByLabIds(IReadOnlyCollection<Guid> labIds)
    {
        ArgumentNullException.ThrowIfNull(labIds);

        lock (lockObject.SyncRoot)
        {
            var labIdSet = new HashSet<Guid>(labIds);
            return _submissions.Values
                .Where(submission => labIdSet.Contains(submission.LabId))
                .Select(Clone)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> ListByStudent(Guid studentId)
    {
        lock (lockObject.SyncRoot)
        {
            return _submissions.Values
                .Where(submission => submission.StudentId == studentId)
                .Select(Clone)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> GetByPairs(IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<Guid> labIds)
    {
        ArgumentNullException.ThrowIfNull(studentIds);
        ArgumentNullException.ThrowIfNull(labIds);

        lock (lockObject.SyncRoot)
        {
            if (studentIds.Count == 0 || labIds.Count == 0)
            {
                return [];
            }

            var result = new List<Submission>();
            var seen = new HashSet<(Guid StudentId, Guid LabId)>();

            // Поиск по индексу пар: только запрошенные пары, без дубликатов входа.
            foreach (var studentId in studentIds)
            {
                foreach (var labId in labIds)
                {
                    if (!seen.Add((studentId, labId)))
                    {
                        continue;
                    }

                    if (_byPair.TryGetValue((studentId, labId), out var id))
                    {
                        result.Add(Clone(_submissions[id]));
                    }
                }
            }

            return result;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Submission> ListForStudentAndSemester(Guid studentId, IReadOnlyCollection<Guid> semesterLabIds)
    {
        ArgumentNullException.ThrowIfNull(semesterLabIds);

        lock (lockObject.SyncRoot)
        {
            if (semesterLabIds.Count == 0)
            {
                return [];
            }

            var labIdSet = new HashSet<Guid>(semesterLabIds);
            return _submissions.Values
                .Where(submission => submission.StudentId == studentId && labIdSet.Contains(submission.LabId))
                .Select(Clone)
                .ToList();
        }
    }

    /// <inheritdoc/>
    public void DeleteByLabId(Guid labId)
    {
        lock (lockObject.SyncRoot)
        {
            DeleteByLabIdCore(labId);
        }
    }

    /// <summary>
    /// Каскад удаления сдач работы. ВЫЗЫВАЕТСЯ ТОЛЬКО из
    /// <see cref="InMemoryLabRepository.Delete"/> под уже удерживаемой блокировкой
    /// хранилища — отдельную блокировку не берёт.
    /// </summary>
    internal void DeleteByLabIdCore(Guid labId)
    {
        var removedIds = _submissions.Values
            .Where(submission => submission.LabId == labId)
            .Select(submission => submission.Id)
            .ToList();

        foreach (var id in removedIds)
        {
            var submission = _submissions[id];
            _submissions.Remove(id);
            _byPair.Remove((submission.StudentId, submission.LabId));
        }
    }

    private static Submission Clone(Submission submission) => new()
    {
        Id = submission.Id,
        StudentId = submission.StudentId,
        LabId = submission.LabId,
        SubmitDate = submission.SubmitDate,
        DefenseDate = submission.DefenseDate,
        UpdatedBy = submission.UpdatedBy,
        UpdatedAt = submission.UpdatedAt,
    };
}
