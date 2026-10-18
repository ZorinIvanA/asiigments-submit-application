using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-147 (P2, boundary; FR-051) «PUT /students/{id}/group: нестроковое groupId →
/// 404 „Группа не найдена“».
/// given: Студент существует.
/// when: PUT /students/{id}/group с телом {groupId: 42} (JSON-число).
/// then: 404 «Группа не найдена». FR-051: «groupId-строка, не равная никакой
/// существующей группе (включая нестроковые значения) → 404 „Группа не найдена“».
/// Регресс-контроль ISS-001 (ADR-016): Kind=Invalid НЕ схлопывается с JsonNull —
/// студент не исключается из группы (не 204).
/// </summary>
public sealed class Ts147_PutStudentGroupNonStringGroupIdTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS147_NonStringGroupId_Returns404GroupNotFound()
    {
        // given: студент существует (DI-сид); сессия teacher.
        var student = B12Seed.EnsureStudent(
            _factory,
            login: "b12s147",
            fullName: "Нестроковый Нестрок Нестрокович",
            email: "b12s147@x.ru");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: PUT /students/{id}/group с телом {groupId: 42} (JSON-число).
        using var put = await client.PutAsync(
            $"/api/v1/students/{student.Id}/group",
            B12Seed.GroupBody("42"));

        // then: 404 «Группа не найдена» (не 204 — не исключение из группы).
        await ApiAssert.AssertMessageAsync(
            put,
            HttpStatusCode.NotFound,
            ErrorTexts.GroupNotFound,
            exactSingleMessageProperty: true);
    }
}
