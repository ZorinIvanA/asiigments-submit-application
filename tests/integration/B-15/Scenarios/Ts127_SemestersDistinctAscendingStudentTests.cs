using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-127 «Семестры: distinct по возрастанию под студентом» (happy_path, FR-018, P0).
///
/// given: демо-сид — работы в семестрах 1 и 2; сессия student01. Изолированное
///        in-memory хранилище фикстуры класса наполняется прямым DI-сидом
///        ILabRepository/IUserRepository тестового хоста (методика зоны,
///        арбитраж a-017/CR-001, ADR-015): в семестре 1 — две работы, чтобы
///        distinct наблюдался на реальном дубликате, сид семестра 2 выполняется
///        раньше — результат не должен зависеть от порядка вставки. Сессия
///        student01 — cookie access_token с JWT HS256, минтым харнесом через
///        ITokenService (IF-003), без POST /auth/login.
/// when:  GET /api/v1/semesters.
/// then:  200 [1,2] — массив различных номеров семестров, по возрастанию
///        (FR-018 AC «Distinct по возрастанию»).
///
/// Примечание (дедупликация): те же AC дословно покрыты каноническим Ts101
/// справочной зоны B-14 (закрепление ревью R4d, см. csproj зоны); файл написан
/// по кейсу текущего переиздания батча B-15 (TS-127).
/// </summary>
public sealed class Ts127_SemestersDistinctAscendingStudentTests : IClassFixture<B15WebAppFactory>
{
    private const string StudentLogin = "student01";
    private const string StudentEmail = "student01@b15.ru";
    private const string StudentFullName = "Студент Сто Двадцать Седьмой";

    private readonly B15WebAppFactory _factory;

    public Ts127_SemestersDistinctAscendingStudentTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Semesters_StudentSession_ReturnsDistinctAscendingIntArray()
    {
        // given: работы в семестрах 1 и 2 (сид семестра 2 — первым); сессия student01.
        B15Harness.SeedLab(_factory, semester: 2, number: 1);
        B15Harness.SeedLab(_factory, semester: 2, number: 3);
        B15Harness.SeedLab(_factory, semester: 1, number: 1);
        B15Harness.SeedLab(_factory, semester: 1, number: 2);

        var student = B15Harness.SeedStudent(
            _factory, fullName: StudentFullName, login: StudentLogin, email: StudentEmail);
        using var client = B15Harness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/semesters под студентом.
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
