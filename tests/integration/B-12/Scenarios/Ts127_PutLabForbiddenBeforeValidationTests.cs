using System.Text;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-127 (P0, negative; FR-022, FR-017) «Порядок проверок: 403 раньше 404 и 400».
/// given: Сессия student (минт, ADR-022); известен несуществующий uuid работы.
/// when:  PUT /api/v1/labs/&lt;несуществующий-id&gt; с невалидным телом (semester=0).
/// then:  403 'Доступ запрещён' — роль проверяется до валидации и существования
///        сущности (FR-022 AC «Порядок 403 раньше 404»).
/// </summary>
public sealed class Ts127_PutLabForbiddenBeforeValidationTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS127_PutUnknownLab_WithInvalidBody_AsStudent_Returns403First()
    {
        // given: сессия student; несуществующий uuid работы; тело с semester=0
        // (нарушает валидацию FR-017 — «Семестр — число от 1 до N»).
        B12Seed.EnsureStudent(
            _factory,
            login: "b12ts127.student",
            fullName: "Порядок Отказов Тестович",
            email: "b12-ts127@t.local");
        var unknownLabId = Guid.NewGuid();
        using var client = HostClients.CreateStudentClient(_factory, "b12ts127.student");

        // when: PUT /api/v1/labs/<несуществующий-id> с невалидным телом.
        using var response = await client.PutAsync(
            $"/api/v1/labs/{unknownLabId}",
            new StringContent("{\"semester\":0}", Encoding.UTF8, "application/json"));

        // then: 403 'Доступ запрещён' — не 400 и не 404.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Forbidden,
            ErrorTexts.Forbidden,
            exactSingleMessageProperty: true);
    }
}
