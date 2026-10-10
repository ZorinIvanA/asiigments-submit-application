using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-203 «Студенты: страница правее последней» (boundary, P1, FR-020).
///
/// given: в выборке 32 студента сида при pageSize=10 (демо-набор: student01..student32);
///        сессия teacher.
/// when:  GET /api/v1/students?page=99.
/// then:  200; items=[]; total=32; page=99; pageSize=10 — пустые items при корректном
///        total (FR-020: «page нормализуется как в FR-017»; FR-017 «страница правее
///        последней — пустые items при корректном total»).
/// </summary>
public sealed class Ts203_StudentsPageBeyondLastTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts203_StudentsPageBeyondLastTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetStudentsPageBeyondLast_ReturnsEmptyItemsWithCorrectTotal()
    {
        // given: сессия teacher; демо-набор с 32 студентами.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: GET /students?page=99.
        using var response = await B06SubmissionsApi.GetStudentsPageAsync(client, page: "99");

        // then: 200; items=[]; total=32; page=99; pageSize=10.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "items", "total", "page", "pageSize");
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
        Assert.Equal(32, root.GetProperty("total").GetInt32());
        Assert.Equal(99, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
    }
}
