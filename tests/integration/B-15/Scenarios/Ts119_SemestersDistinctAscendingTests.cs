using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;
using System.Text.Json;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-119 «Семестры: distinct по возрастанию под student» (happy_path, FR-018, P0).
///
/// given: сид — работы в семестрах 1 и 2 (по несколько в семестре 1 — distinct
///        проверяется на реальных дублях; сид семестра 2 выполняется раньше сида
///        семестра 1 — результат не должен зависеть от порядка вставки); DI-сид
///        IUserRepository/ILabRepository тестового хоста (CR-001, ADR-015); сессия
///        student01 — cookie access_token с JWT HS256, минтым харнесом (ITokenService).
/// when:  GET /api/v1/semesters.
/// then:  200; тело [1,2] — массив различных номеров семестров по возрастанию
///        (FR-018 AC «Distinct по возрастанию»).
/// </summary>
public sealed class Ts119_SemestersDistinctAscendingTests : IClassFixture<B15WebAppFactory>
{
    private const string StudentLogin = "student01";

    private readonly B15WebAppFactory _factory;

    public Ts119_SemestersDistinctAscendingTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Semesters_ReturnsDistinctAscendingUnderStudent()
    {
        // given: работы в семестрах 1 и 2 (сид семестра 2 — первым); студент student01.
        B15Harness.SeedLab(_factory, semester: 2, number: 1);
        B15Harness.SeedLab(_factory, semester: 2, number: 3);
        B15Harness.SeedLab(_factory, semester: 1, number: 1);
        B15Harness.SeedLab(_factory, semester: 1, number: 2);

        var student = B15Harness.SeedStudent(
            _factory, fullName: "Студент Первый", login: StudentLogin, email: "student01@b15.ru");
        using var client = B15Harness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/semesters под student.
        using var response = await client.GetAsync(B15Harness.SemestersEndpoint);

        // then: 200; тело — JSON-массив целых [1,2]: distinct и по возрастанию.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        var semesters = document.RootElement.EnumerateArray()
            .Select(element =>
            {
                Assert.Equal(JsonValueKind.Number, element.ValueKind);
                return element.GetInt32();
            })
            .ToList();

        Assert.Equal(new[] { 1, 2 }, semesters);
    }
}
