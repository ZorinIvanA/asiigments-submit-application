using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-150 (P0, negative; FR-021/IF-001, v2.2) «GET /submissions: неизвестный
/// groupId → 404; отсутствующий groupId → 400 полевой валидации».
/// given: Случайный uuid; сессия teacher.
/// when: GET /api/v1/submissions?groupId=&lt;случайный-uuid&gt;&amp;semester=1; отдельно
///       без groupId.
/// then: неизвестный uuid → 404 «Группа не найдена» (сущность не найдена);
///       отсутствующий параметр → 400 «Данные заполнены неверно» с
///       errors.groupId «Заполните поле» (обязательное поле — полевая валидация,
///       а не 404 сущности; ASM-018/IF-001 v2.2).
/// </summary>
public sealed class Ts150_SubmissionsGroupNotFoundTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS150_UnknownGroupId_Returns404GroupNotFound()
    {
        // given: случайный uuid (ни одна группа не существует — демо-сид выключен); сессия teacher.
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /submissions?groupId=<случайный-uuid>&semester=1.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={Uri.EscapeDataString(Guid.NewGuid().ToString())}&semester=1");

        // then: 404 «Группа не найдена» (конверт ровно {message}).
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.NotFound,
            ErrorTexts.GroupNotFound,
            exactSingleMessageProperty: true);
    }

    [Fact]
    public async Task TS150_MissingGroupId_Returns400RequiredFieldError()
    {
        // given: сессия teacher; параметр groupId отсутствует.
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /submissions?semester=1 (без groupId).
        using var response = await client.GetAsync("/api/v1/submissions?semester=1");

        // then: 400 «Данные заполнены неверно» c errors.groupId «Заполните поле»
        //       (v2.2/IF-001: отсутствующий обязательный параметр запроса — полевая
        //       валидация; 404 «Группа не найдена» — только для НЕИЗВЕСТНОЙ группы).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal(ErrorTexts.InvalidData, body.GetProperty("message").GetString());
        var groupIdErrors = body.GetProperty("errors").GetProperty("groupId");
        Assert.Equal(1, groupIdErrors.GetArrayLength());
        Assert.Equal(ErrorTexts.Required, groupIdErrors[0].GetString());
    }
}
