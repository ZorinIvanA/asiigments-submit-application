using System.Text;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-127 (P0, negative; FR-022, FR-017) «Порядок проверок: 403 раньше 404 и 400».
/// given: Сессия student (минт, ADR-022); известен несуществующий uuid работы.
/// when:  PUT /api/v1/labs/&lt;несуществующий-id&gt; с невалидным телом (semester=0).
/// then:  403 'Доступ запрещён' — роль проверяется ДО валидации тела (400) и
///        поиска записи (404) (FR-022 AC «Порядок 403 раньше 404»).
/// </summary>
public sealed class Ts127_PutLabForbiddenBeforeValidationTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS127_PutUnknownLab_WithInvalidBody_AsStudent_Returns403First()
    {
        // given: сессия student; несуществующий uuid работы; тело с semester=0
        // (нарушает валидацию FR-017 — «Семестр — число от 1 до N»).
        B10Seed.AddStudent(_factory, "b10ts127.student");
        var unknownLabId = Guid.NewGuid();
        using var client = HostClients.CreateStudentClient(_factory, "b10ts127.student");

        // when: PUT /api/v1/labs/<несуществующий-id> с невалидным телом.
        using var response = await client.PutAsync(
            $"/api/v1/labs/{unknownLabId}",
            new StringContent("{\"semester\":0}", Encoding.UTF8, "application/json"));

        // then: 403 'Доступ запрещён' — не 400 и не 404.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Forbidden,
            "Доступ запрещён",
            exactSingleMessageProperty: true);
    }
}
