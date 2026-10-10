using System.Net.Http.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-195 «Идентификаторы не-uuid формата в пути и теле — 404 сущности, не 500»
/// (negative, P1, FR-020/FR-021).
///
/// given: teacher-сессия; существует student01 (его uuid известен тесту);
///        подготовлена не-uuid строка 'not-a-uuid' для сегмента пути и полей тела.
/// when:  PUT /api/v1/students/not-a-uuid/group {groupId:null}; затем
///        PUT /api/v1/submissions {studentId:'not-a-uuid', labId:'not-a-uuid',
///        submitDate:null, defenseDate:null}; затем
///        PUT /api/v1/students/{uuid student01}/group {groupId:'not-a-uuid'}.
/// then:  первый и второй — 404 'Студент не найден' (не-uuid строка не
///        идентифицирует ни одного пользователя; в PUT /submissions студент
///        разрешается после дат — null-даты формально валидны — и раньше
///        работы); третий — 404 'Группа не найдена'. Ни одного 500 и ни одного
///        400: не-uuid трактуется как несуществующая сущность.
/// </summary>
public sealed class Ts195_NonUuidIdentifiersResolveToEntityNotFoundTests :
    IClassFixture<B06SubmissionsWebAppFactory>
{
    private const string NotAUuid = "not-a-uuid";

    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _student01Id;

    public Ts195_NonUuidIdentifiersResolveToEntityNotFoundTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        var student01 = B06SubmissionsSessions.RequireUser(factory, "student01");
        Assert.Equal(UserRoles.Student, student01.Role);
        _student01Id = student01.Id;
    }

    [Fact]
    public async Task NonUuidIdentifierInPathAndBody_ReturnsEntity404Never500()
    {
        // given: teacher-сессия; валидный student01; не-uuid строка подготовлена.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: PUT /students/not-a-uuid/group {groupId:null}.
        using var pathSegment = await PutStudentGroupAsync(client, NotAUuid, groupId: null);

        // then: 404 'Студент не найден' — не-uuid не идентифицирует пользователя (не 500, не 400).
        await AssertEntityNotFoundAsync(pathSegment, "Студент не найден");

        // when: PUT /submissions {studentId:'not-a-uuid', labId:'not-a-uuid', null, null}.
        using var submissionBody = await B06SubmissionsApi.PutSubmissionAsync(
            client,
            studentId: NotAUuid,
            labId: NotAUuid,
            submitDate: null,
            defenseDate: null);

        // then: 404 'Студент не найден' — даты (null) валидны, студент разрешается раньше работы.
        await AssertEntityNotFoundAsync(submissionBody, "Студент не найден");

        // when: PUT /students/{uuid student01}/group {groupId:'not-a-uuid'}.
        using var groupBody = await PutStudentGroupAsync(client, _student01Id.ToString(), NotAUuid);

        // then: 404 'Группа не найдена' — строка groupId не существует как группа.
        await AssertEntityNotFoundAsync(groupBody, "Группа не найдена");
    }

    /// <summary>PUT /api/v1/students/{id}/group {groupId} — null уходит в JSON null (снятие группы).</summary>
    private static Task<HttpResponseMessage> PutStudentGroupAsync(
        HttpClient client, string studentId, string? groupId) =>
        client.PutAsJsonAsync(
            $"/api/v1/students/{Uri.EscapeDataString(studentId)}/group",
            new { groupId });

    /// <summary>Ровно 404 (не 500 и не 400) с message из словаря текстов сущностей.</summary>
    private static async Task AssertEntityNotFoundAsync(HttpResponseMessage response, string expectedMessage)
    {
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, expectedMessage);
    }
}
