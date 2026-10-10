using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-141 (P1, negative; FR-050) «GET /students: преподаватели исключены из выборки».
/// given: В хранилище teacher и 5 студентов; сессия teacher.
/// when: GET /students без фильтров.
/// then: total=5; в items преподавателя нет (только role=student).
/// FR-050 AC «Преподаватели исключены».
/// </summary>
public sealed class Ts141_StudentsExcludeTeachersTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS141_ListWithoutFilters_ExcludesTeacher()
    {
        // given: в хранилище сид-teacher (FR-004) и ровно 5 студентов (DI-сид; демо-набор выключен).
        foreach (var index in Enumerable.Range(1, 5))
        {
            B12Seed.EnsureStudent(
                _factory,
                login: $"b12s141_{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                fullName: $"Студент Пятьсот {index}",
                email: $"b12s141_{index}@x.ru");
        }

        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /students без фильтров.
        using var response = await client.GetAsync("/api/v1/students");

        // then: total=5; преподавателя в items нет.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(5, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(5, items.GetArrayLength());
        var logins = items.EnumerateArray()
            .Select(item => item.GetProperty("login").GetString())
            .ToArray();
        Assert.DoesNotContain(SeedOptions.DefaultTeacherLogin, logins);
        Assert.All(logins, login => Assert.StartsWith("b12s141_", login, StringComparison.Ordinal));
    }
}
