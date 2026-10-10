using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-127 (P0, negative; FR-022, FR-017) «Порядок проверок: 403 раньше 404 и 400».
/// given: Сессия student (минт, ADR-015); известен несуществующий uuid работы.
/// when:  PUT /api/v1/labs/&lt;несуществующий-id&gt; с невалидным телом (semester=0).
/// then:  403 «Доступ запрещён» — роль проверяется до валидации и существования
///        сущности (FR-022 AC «Порядок 403 раньше 404»).
/// </summary>
public sealed class Ts127_ForbiddenBeforeValidationNotFoundTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS127_PutLab_AsStudent_UnknownId_InvalidBody_Returns403First()
    {
        // given: сессия student; несуществующий uuid работы.
        B11AuthSessions.SeedStudent(_factory, "b11ts127.student", "Порядок Отказов");
        using var client = B11AuthSessions.CreateSessionClient(_factory, "b11ts127.student");
        var unknownLabId = Guid.NewGuid();

        // when: PUT /api/v1/labs/<несуществующий-id> с невалидным телом (semester=0).
        using var response = await B11ApiCalls.SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/labs/{unknownLabId}",
            "{\"number\":1,\"semester\":0,\"content\":\"Порядок отказов\"," +
            "\"assignmentUrl\":\"\",\"defenseRequired\":false}");

        // then: 403 «Доступ запрещён» — и невалидность тела, и несуществование
        // сущности остались непроверенными (порядок 401→403→400→404→409).
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Forbidden,
            "Доступ запрещён",
            exactSingleMessageProperty: true);
    }
}
