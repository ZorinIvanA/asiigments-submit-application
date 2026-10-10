using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-122 (P1, boundary; FR-035) «GET /semesters: пустой массив без лаб».
/// given: Лабораторных нет (хост без демо-набора, DI-сид лаб не выполняется); сессия student.
/// when: GET /api/v1/semesters.
/// then: 200 [] (роль student не мешает — 200, не 403; FR-035 AC «Пусто»).
/// </summary>
public sealed class Ts122_SemestersEmptyTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS122_GetSemesters_WithoutLabs_ReturnsEmptyArray()
    {
        // given: лабораторных нет; сессия student.
        B10Seed.AddStudent(_factory, B10Seed.StudentLogin);
        using var client = HostClients.CreateStudentClient(_factory, B10Seed.StudentLogin);

        // when: GET /api/v1/semesters.
        using var response = await client.GetAsync("/api/v1/semesters");

        // then: 200 [] — пустой массив (не 403, не 404).
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }
}
