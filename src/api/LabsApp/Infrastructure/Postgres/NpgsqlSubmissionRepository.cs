using LabsApp.Domain.Entities;
using Npgsql;

namespace LabsApp.Storage.Postgres;

/// <summary>
/// Реализация <see cref="ISubmissionRepository"/> на «чистом» ADO.NET:
/// NpgsqlDataSource + NpgsqlCommand/NpgsqlDataReader, без EF Core. Все операции
/// асинхронны (ExecuteReaderAsync/ReadAsync/ExecuteNonQueryAsync). Upsert —
/// INSERT … ON CONFLICT (student_id, lab_id) DO UPDATE … RETURNING: атомарность
/// пары гарантирует уникальный констрейнт схемы (src/api/db/02_schema.sql).
/// </summary>
public sealed class NpgsqlSubmissionRepository(NpgsqlDataSource dataSource) : ISubmissionRepository
{
    public async Task<Submission?> UpsertAsync(
        Guid studentId,
        Guid labId,
        DateOnly? submitDate,
        DateOnly? defenseDate,
        Guid? updatedBy,
        DateTime updatedAt)
    {
        const string sql = """
            INSERT INTO submissions (id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at)
            VALUES (@id, @student_id, @lab_id, @submit_date, @defense_date, @updated_by, @updated_at)
            ON CONFLICT (student_id, lab_id) DO UPDATE SET
                submit_date  = EXCLUDED.submit_date,
                defense_date = EXCLUDED.defense_date,
                updated_by   = EXCLUDED.updated_by,
                updated_at   = EXCLUDED.updated_at
            RETURNING id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("student_id", studentId);
        command.Parameters.AddWithValue("lab_id", labId);
        command.Parameters.Add(new("submit_date", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)submitDate ?? DBNull.Value });
        command.Parameters.Add(new("defense_date", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)defenseDate ?? DBNull.Value });
        command.Parameters.AddWithValue("updated_by", (object?)updatedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("updated_at", updatedAt);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return MapSubmission(reader);
    }

    public async Task<Submission?> GetByStudentAndLabAsync(Guid studentId, Guid labId)
    {
        const string sql = """
            SELECT id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at
            FROM submissions
            WHERE student_id = @student_id AND lab_id = @lab_id
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("student_id", studentId);
        command.Parameters.AddWithValue("lab_id", labId);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSubmission(reader) : null;
    }

    public async Task<IReadOnlyList<Submission>> ListByLabIdsAsync(IReadOnlyCollection<Guid> labIds)
    {
        ArgumentNullException.ThrowIfNull(labIds);
        if (labIds.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at
            FROM submissions
            WHERE lab_id = ANY(@lab_ids)
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("lab_ids", labIds.ToArray());

        return await ReadAllAsync(command);
    }

    public async Task<IReadOnlyList<Submission>> ListByStudentAsync(Guid studentId)
    {
        const string sql = """
            SELECT id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at
            FROM submissions
            WHERE student_id = @student_id
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("student_id", studentId);

        return await ReadAllAsync(command);
    }

    public async Task<IReadOnlyList<Submission>> GetByPairsAsync(
        IReadOnlyCollection<Guid> studentIds,
        IReadOnlyCollection<Guid> labIds)
    {
        ArgumentNullException.ThrowIfNull(studentIds);
        ArgumentNullException.ThrowIfNull(labIds);
        if (studentIds.Count == 0 || labIds.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at
            FROM submissions
            WHERE student_id = ANY(@student_ids) AND lab_id = ANY(@lab_ids)
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("student_ids", studentIds.ToArray());
        command.Parameters.AddWithValue("lab_ids", labIds.ToArray());

        return await ReadAllAsync(command);
    }

    public async Task<IReadOnlyList<Submission>> ListForStudentAndSemesterAsync(
        Guid studentId,
        IReadOnlyCollection<Guid> semesterLabIds)
    {
        ArgumentNullException.ThrowIfNull(semesterLabIds);
        if (semesterLabIds.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at
            FROM submissions
            WHERE student_id = @student_id AND lab_id = ANY(@lab_ids)
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("student_id", studentId);
        command.Parameters.AddWithValue("lab_ids", semesterLabIds.ToArray());

        return await ReadAllAsync(command);
    }

    public async Task DeleteByLabIdAsync(Guid labId)
    {
        const string sql = "DELETE FROM submissions WHERE lab_id = @lab_id";

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("lab_id", labId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Чтение всех строк команды в список снимков (reader закрывает команда).</summary>
    private static async Task<IReadOnlyList<Submission>> ReadAllAsync(NpgsqlCommand command)
    {
        var result = new List<Submission>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapSubmission(reader));
        }

        return result;
    }

    /// <summary>Разбор строки reader в сущность (порядок колонок — как в SELECT).</summary>
    private static Submission MapSubmission(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        StudentId = reader.GetGuid(1),
        LabId = reader.GetGuid(2),
        SubmitDate = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
        DefenseDate = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
        UpdatedBy = reader.IsDBNull(5) ? null : reader.GetGuid(5),
        UpdatedAt = reader.GetFieldValue<DateTime>(6),
    };
}
