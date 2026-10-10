using System.Globalization;
using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B14.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-101 «semesters: distinct по возрастанию; анонимно 401» (happy_path, FR-018, P0).
///
/// given: DI-сид — работы в семестрах 1 и 2; сессия student (минт access-JWT,
///        ADR-022); анонимный запрос.
/// when:  GET /api/v1/semesters под student; анонимно.
/// then:  Под student — 200 [1,2] (массив целых, по возрастанию); анонимно —
///        401 «Не авторизован». FR-018 AC.
/// </summary>
public sealed class Ts101_SemestersDistinctAscendingAuthTests : IClassFixture<B14WebAppFactory>
{
    private const string Login = "ts101";
    private const string Email = "ts101@x.ru";
    private const string FullName = "Студент Сто Первый";
    private const string SemestersEndpoint = "/api/v1/semesters";

    private readonly B14WebAppFactory _factory;

    public Ts101_SemestersDistinctAscendingAuthTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Semesters_StudentSession_ReturnsDistinctAscendingIntArray()
    {
        // given: работы в семестрах 1 и 2 (в семестре 1 две работы — distinct
        // наблюдаем; DI-сид в ILabRepository, ADR-010); сессия student.
        AddLab(semester: 1, number: 1);
        AddLab(semester: 1, number: 2);
        AddLab(semester: 2, number: 1);
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.CreateSessionClient(_factory, seeded.Id, UserRoles.Student);

        // when: GET /api/v1/semesters под student.
        using var response = await client.GetAsync(SemestersEndpoint);

        // then: 200 — массив целых [1, 2], distinct по возрастанию (FR-018 AC).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var body = document.RootElement;
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        var semesters = body.EnumerateArray()
            .Select(item =>
            {
                Assert.Equal(JsonValueKind.Number, item.ValueKind);
                Assert.True(
                    item.TryGetInt32(out var semester),
                    "Элемент массива семестров — не целое число.");
                return semester;
            })
            .ToArray();
        Assert.Equal(new[] { 1, 2 }, semesters);
    }

    [Fact]
    public async Task Semesters_AnonymousRequest_Returns401Unauthorized()
    {
        // given: анонимный запрос (без cookie access_token).
        using var client = B14Harness.Create(_factory);

        // when: GET /api/v1/semesters анонимно.
        using var response = await client.GetAsync(SemestersEndpoint);

        // then: 401 «Не авторизован» дословно (FR-018 AC, IF-001).
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B14Assertions.ReadRootObjectAsync(response);
        B14Assertions.MessageIs(root, ErrorTexts.Unauthorized);
    }

    /// <summary>DI-сид работы (ADR-010): прямая вставка в ILabRepository тестового хоста.</summary>
    private void AddLab(int semester, int number)
    {
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = string.Create(CultureInfo.InvariantCulture, $"Содержание (DI-сид) {semester}:{number}"),
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _factory.Services.GetRequiredService<ILabRepository>().Add(lab);
    }
}
