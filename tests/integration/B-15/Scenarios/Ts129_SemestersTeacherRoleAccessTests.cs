using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-129 «Семестры: доступно любой роли (teacher)» (boundary, FR-018/FR-022, P1).
///
/// given: сессия teacher. Контекст данных FR-018 тот же, что у TS-127 (работы в
///        семестрах 1 и 2): изолированное in-memory хранилище фикстуры класса
///        наполняется прямым DI-сидом ILabRepository/IUserRepository тестового
///        хоста (методика зоны, арбитраж a-017/CR-001, ADR-015). Сессия
///        преподавателя минтится харнесом тем же способом (cookie access_token,
///        JWT HS256 c role=teacher, ITokenService, IF-003) — без POST /auth/login.
/// when:  GET /api/v1/semesters.
/// then:  200 [1,2] (FR-018: «Доступно любой авторизованной роли»; FR-022:
///        semesters — role=user, роль дополнительно не проверяется, 403 нет).
/// </summary>
public sealed class Ts129_SemestersTeacherRoleAccessTests : IClassFixture<B15WebAppFactory>
{
    private const string TeacherLogin = "teacher01";
    private const string TeacherEmail = "teacher01@b15.ru";
    private const string TeacherFullName = "Преподаватель Сто Двадцать Девятый";

    private readonly B15WebAppFactory _factory;

    public Ts129_SemestersTeacherRoleAccessTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Semesters_TeacherSession_ReturnsDistinctAscendingIntArray()
    {
        // given: работы в семестрах 1 и 2; преподаватель; сессия teacher.
        B15Harness.SeedLab(_factory, semester: 2, number: 1);
        B15Harness.SeedLab(_factory, semester: 2, number: 3);
        B15Harness.SeedLab(_factory, semester: 1, number: 1);
        B15Harness.SeedLab(_factory, semester: 1, number: 2);

        var teacher = SeedTeacher(
            login: TeacherLogin, email: TeacherEmail, fullName: TeacherFullName);
        using var client = B15Harness.CreateSessionClient(_factory, teacher.Id, UserRoles.Teacher);

        // when: GET /api/v1/semesters под teacher.
        using var response = await client.GetAsync(B15Harness.SemestersEndpoint);

        // then: 200 (не 401/403); тело — JSON-массив целых [1,2], distinct по возрастанию.
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

    /// <summary>
    /// DI-сид преподавателя (методика зоны ADR-015/CR-001): прямая вставка в
    /// IUserRepository тестового хоста; пароль кейсом не используется —
    /// маркер-заглушка харнеса (B15Harness.SeededPasswordHashMark).
    /// </summary>
    private User SeedTeacher(string login, string email, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = B15Harness.SeededPasswordHashMark,
            FullName = fullName,
            Role = UserRoles.Teacher,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };

        _factory.Services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }
}
