using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-146 (P0, negative; FR-051) «PUT /students/{id}/group: неизвестная группа →
/// 404 „Группа не найдена“».
/// given: Студент существует; неизвестный uuid группы.
/// when: PUT /students/{id}/group {groupId:'&lt;неизвестный-uuid&gt;'}.
/// then: 404 «Группа не найдена». FR-051 AC «Группа не найдена».
/// </summary>
public sealed class Ts146_PutStudentGroupGroupNotFoundTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS146_UnknownGroupId_Returns404GroupNotFound()
    {
        // given: студент существует (DI-сид); неизвестный uuid группы; сессия teacher.
        var student = B12Seed.EnsureStudent(
            _factory,
            login: "b12s146",
            fullName: "Без Группы Неизвестногрупович",
            email: "b12s146@x.ru");
        var unknownGroupId = Guid.NewGuid();
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: PUT /students/{id}/group {groupId:'<неизвестный-uuid>'}.
        using var put = await client.PutAsync(
            $"/api/v1/students/{student.Id}/group",
            B12Seed.GroupBody($"\"{unknownGroupId}\""));

        // then: 404 «Группа не найдена».
        await ApiAssert.AssertMessageAsync(
            put,
            HttpStatusCode.NotFound,
            ErrorTexts.GroupNotFound,
            exactSingleMessageProperty: true);
    }
}
