using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-145 (P0, negative; FR-051) «PUT /students/{id}/group: нестуденческий и
/// неизвестный id → 404 „Студент не найден“».
/// given: uuid преподавателя; случайный uuid; группа X существует.
/// when: PUT /students/&lt;uuid-преподавателя&gt;/group {groupId:X}; отдельно
/// PUT /students/&lt;случайный-uuid&gt;/group {groupId:X}.
/// then: Оба → 404 «Студент не найден». FR-051 AC «Студент не найден».
/// </summary>
public sealed class Ts145_PutStudentGroupStudentNotFoundTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS145_TeacherUuid_Returns404StudentNotFound()
    {
        // given: uuid преподавателя; группа X существует; сессия teacher.
        var teacherId = B12Seed.TeacherId(_factory);
        var groupX = B12Seed.EnsureGroup(_factory, "B12-X");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: PUT /students/<uuid-преподавателя>/group {groupId:X}.
        using var put = await client.PutAsync(
            $"/api/v1/students/{teacherId}/group",
            B12Seed.GroupBody($"\"{groupX.Id}\""));

        // then: 404 «Студент не найден» (роль != student приравнена к отсутствию).
        await ApiAssert.AssertMessageAsync(
            put,
            HttpStatusCode.NotFound,
            ErrorTexts.StudentNotFound,
            exactSingleMessageProperty: true);
    }

    [Fact]
    public async Task TS145_UnknownUuid_Returns404StudentNotFound()
    {
        // given: случайный uuid (пользователя нет); группа X существует; сессия teacher.
        var unknownId = Guid.NewGuid();
        var groupX = B12Seed.EnsureGroup(_factory, "B12-X");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: PUT /students/<случайный-uuid>/group {groupId:X}.
        using var put = await client.PutAsync(
            $"/api/v1/students/{unknownId}/group",
            B12Seed.GroupBody($"\"{groupX.Id}\""));

        // then: 404 «Студент не найден».
        await ApiAssert.AssertMessageAsync(
            put,
            HttpStatusCode.NotFound,
            ErrorTexts.StudentNotFound,
            exactSingleMessageProperty: true);
    }
}
