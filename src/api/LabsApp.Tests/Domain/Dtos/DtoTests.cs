using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using LabsApp.Domain.Dtos;
using LabsApp.Domain.Entities;

namespace LabsApp.Tests.Domain.Dtos;

/// <summary>
/// Юнит-проверки DTO (T-002): нормализация page (критерий «Нормализация page») и
/// транспортных форм — имена полей дословно по контрактам IF/ASM-005 (даты
/// DateOnly → 'YYYY-MM-DD', ADR-005). SubmissionDto — БЕЗ null-формы (реворк
/// FR-061): PUT всегда возвращает полную запись, id/updatedAt/updatedBy ненулевые.
/// </summary>
public sealed class DtoTests
{
    /// <summary>Опции сериализации — как в Program.cs (глобальный camelCase).</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ------------------------------------------------------------------
    // Нормализация page (PagedResult.Normalize)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2147483648")]
    [InlineData("1.0")]
    public void Normalize_InvalidRawPage_FallsBackToOne(string? rawPage)
    {
        Assert.Equal(1, PagedResult<string>.Normalize(rawPage));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("10", 10)]
    [InlineData(" 3 ", 3)]
    [InlineData("007", 7)]
    [InlineData("+5", 5)]
    [InlineData("2147483647", 2147483647)]
    public void Normalize_ValidRawPage_ParsesValue(string rawPage, int expected)
    {
        Assert.Equal(expected, PagedResult<string>.Normalize(rawPage));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-7, 1)]
    [InlineData(1, 1)]
    [InlineData(9, 9)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Normalize_NumericPage_ClampsBelowOne(int page, int expected)
    {
        Assert.Equal(expected, PagedResult<string>.Normalize(page));
    }

    [Fact]
    public void PagedResult_SerializesContractShape()
    {
        var paged = new PagedResult<StudentDto>
        {
            Items = [new StudentDto { Id = "u1", FullName = "Иванов Иван", Login = "student01", Email = "s01@example.com" }],
            Total = 11,
            Page = 2,
            PageSize = 10,
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(paged, SerializerOptions));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(11, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());

        var item = items[0];
        foreach (var key in new[] { "id", "fullName", "login", "email", "groupId", "groupName" })
        {
            Assert.True(item.TryGetProperty(key, out _), $"Ожидалось поле {key} в StudentDto.");
        }

        Assert.Equal(JsonValueKind.Null, item.GetProperty("groupId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("groupName").ValueKind);
    }

    // ------------------------------------------------------------------
    // SubmissionDto: полная запись без null-формы (реворк FR-061), форматы дат (ADR-005)
    // ------------------------------------------------------------------

    [Fact]
    public void SubmissionDto_AlwaysFullForm_IdUpdatedAtUpdatedByNotNull()
    {
        // Реворк FR-061: PUT всегда возвращает полную запись; id/updatedAt/updatedBy
        // ненулевые; сброшенные даты сериализуются JSON-null.
        var dto = new SubmissionDto
        {
            Id = "submission-uuid",
            StudentId = "student-uuid",
            LabId = "lab-uuid",
            SubmitDate = new DateOnly(2026, 10, 1),
            DefenseDate = null,
            UpdatedAt = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc),
            UpdatedBy = "teacher-uuid",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        Assert.Equal(7, root.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.String, root.GetProperty("id").ValueKind);
        Assert.Equal("submission-uuid", root.GetProperty("id").GetString());
        Assert.Equal("student-uuid", root.GetProperty("studentId").GetString());
        Assert.Equal("lab-uuid", root.GetProperty("labId").GetString());
        Assert.Equal("2026-10-01", root.GetProperty("submitDate").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("defenseDate").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("updatedAt").ValueKind);
        Assert.Equal("2026-10-01T12:30:00Z", root.GetProperty("updatedAt").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("updatedBy").ValueKind);
        Assert.Equal("teacher-uuid", root.GetProperty("updatedBy").GetString());
    }

    [Fact]
    public void SubmissionDto_ResetDates_SerializeAsJsonNull_WithNonNullableMeta()
    {
        // Сброс обеих дат — запись остаётся: submitDate/defenseDate = null,
        // id/updatedAt/updatedBy при этом заполнены (null-формы больше нет).
        var dto = new SubmissionDto
        {
            Id = "submission-uuid",
            StudentId = "student-uuid",
            LabId = "lab-uuid",
            SubmitDate = null,
            DefenseDate = null,
            UpdatedAt = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc),
            UpdatedBy = "teacher-uuid",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        Assert.Equal(7, root.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("submitDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("defenseDate").ValueKind);
        Assert.Equal("2026-10-02T08:00:00Z", root.GetProperty("updatedAt").GetString());
        Assert.Equal("teacher-uuid", root.GetProperty("updatedBy").GetString());
    }

    // ------------------------------------------------------------------
    // Транспортные формы остальных DTO: ровно контрактный набор ключей
    // ------------------------------------------------------------------

    [Fact]
    public void MeDto_SerializesContractShape()
    {
        var dto = new MeDto { Login = "student01", FullName = "Иванов Иван", Role = UserRoles.Student, GroupName = null };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("student01", root.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван", root.GetProperty("fullName").GetString());
        Assert.Equal("student", root.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("groupName").ValueKind);
    }

    [Fact]
    public void ProfileDto_SerializesContractShape()
    {
        var dto = new ProfileDto
        {
            Login = "student01",
            Email = "s01@example.com",
            FullName = "Иванов Иван",
            Role = UserRoles.Student,
            GroupName = "ИК-221",
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Equal("s01@example.com", root.GetProperty("email").GetString());
        Assert.Equal("ИК-221", root.GetProperty("groupName").GetString());
    }

    [Fact]
    public void LabDto_SerializesContractShape()
    {
        var dto = new LabDto
        {
            Id = "lab-uuid",
            Semester = 3,
            Number = 1,
            Content = "Новая работа",
            AssignmentUrl = null,
            DefenseRequired = true,
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        Assert.Equal(6, root.EnumerateObject().Count());
        Assert.Equal("lab-uuid", root.GetProperty("id").GetString());
        Assert.Equal(3, root.GetProperty("semester").GetInt32());
        Assert.Equal(1, root.GetProperty("number").GetInt32());
        Assert.Equal("Новая работа", root.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("assignmentUrl").ValueKind);
        Assert.True(root.GetProperty("defenseRequired").GetBoolean());
    }

    [Fact]
    public void GroupDto_SerializesContractShape()
    {
        var dto = new GroupDto { Id = "group-uuid", Name = "ИК-221", StudentCount = 5 };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.Equal("group-uuid", root.GetProperty("id").GetString());
        Assert.Equal("ИК-221", root.GetProperty("name").GetString());
        Assert.Equal(5, root.GetProperty("studentCount").GetInt32());
    }

    [Fact]
    public void SubmissionsGridDto_SerializesContractShape()
    {
        var dto = new SubmissionsGridDto
        {
            Students = [new GridStudentDto { Id = "u1", FullName = "Иванов Иван" }],
            Labs = [new GridLabDto { Id = "lab-uuid", Number = 1, DefenseRequired = false }],
            Submissions = [new GridSubmissionDto
            {
                StudentId = "u1",
                LabId = "lab-uuid",
                SubmitDate = new DateOnly(2026, 10, 2),
                DefenseDate = null,
            }],
            Total = 5,
            Page = 1,
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        foreach (var key in new[] { "students", "labs", "submissions", "total", "page" })
        {
            Assert.True(root.TryGetProperty(key, out _), $"Ожидалось поле {key} в SubmissionsGridDto.");
        }

        var submission = root.GetProperty("submissions")[0];
        Assert.Equal("u1", submission.GetProperty("studentId").GetString());
        Assert.Equal("lab-uuid", submission.GetProperty("labId").GetString());
        Assert.Equal("2026-10-02", submission.GetProperty("submitDate").GetString());
        Assert.Equal(JsonValueKind.Null, submission.GetProperty("defenseDate").ValueKind);
        Assert.Equal(5, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
    }

    [Fact]
    public void MySubmissionsDto_SerializesContractShape()
    {
        var dto = new MySubmissionsDto
        {
            HasGroup = true,
            Labs = [new GridLabDto { Id = "lab-uuid", Number = 2, DefenseRequired = true }],
            Submissions = [new MySubmissionDto { LabId = "lab-uuid", SubmitDate = null, DefenseDate = null }],
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, SerializerOptions));
        var root = doc.RootElement;

        foreach (var key in new[] { "hasGroup", "labs", "submissions" })
        {
            Assert.True(root.TryGetProperty(key, out _), $"Ожидалось поле {key} в MySubmissionsDto.");
        }

        Assert.True(root.GetProperty("hasGroup").GetBoolean());
        var submission = root.GetProperty("submissions")[0];
        Assert.Equal(3, submission.EnumerateObject().Count());
        Assert.Equal("lab-uuid", submission.GetProperty("labId").GetString());
    }
}
