using System.Text.Json;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-198 «Лабораторные: GET по id существующей записи — 200 LabDto» (happy_path, P1, FR-017).
///
/// given: сид развёрнут (демо-набор); известна запись семестра 1 номера 1
///        (id получен из GET /api/v1/labs?semester=1: первый элемент items);
///        сессия teacher.
/// when:  GET /api/v1/labs/{id}.
/// then:  200; тело LabDto {id, semester:1, number:1,
///        content:'Содержание лабораторной работы №1', assignmentUrl:null (N=1 не кратно 5),
///        defenseRequired:false (N=1 нечётное)} — поле-в-поле соответствует той же записи
///        в списке GET /labs; camelCase, без лишних полей.
/// </summary>
public sealed class Ts198_GetLabByIdTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private const string LabContentNumber1 = "Содержание лабораторной работы №1";

    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts198_GetLabByIdTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetLabById_ReturnsLabDtoEqualToListItem()
    {
        // given: сессия teacher; id записи (1,1) — первый элемент items списка GET /labs?semester=1.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        using var listResponse = await B06SubmissionsApi.GetLabsBySemesterAsync(client, semester: "1");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await BodyAssertions.ReadRootObjectAsync(listResponse);
        var listItem = B06SubmissionsApi.GetArrayElement(list, "items", index: 0);
        Assert.Equal(1, listItem.GetProperty("semester").GetInt32());
        Assert.Equal(1, listItem.GetProperty("number").GetInt32());
        var labId = listItem.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(labId), "Ожидался непустой id работы в элементе списка.");

        // when: GET /labs/{id}.
        using var response = await B06SubmissionsApi.GetLabByIdAsync(client, labId!);

        // then: 200; LabDto без лишних полей, значения — как у той же записи в списке.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lab = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(
            lab, "id", "semester", "number", "content", "assignmentUrl", "defenseRequired");

        Assert.Equal(labId, lab.GetProperty("id").GetString());
        Assert.Equal(1, lab.GetProperty("semester").GetInt32());
        Assert.Equal(1, lab.GetProperty("number").GetInt32());
        Assert.Equal(LabContentNumber1, lab.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, lab.GetProperty("assignmentUrl").ValueKind);
        Assert.Equal(JsonValueKind.False, lab.GetProperty("defenseRequired").ValueKind);

        // Поле-в-поле с записью списка (id получен из него).
        Assert.Equal(
            listItem.GetProperty("content").GetString(),
            lab.GetProperty("content").GetString());
        Assert.Equal(
            listItem.GetProperty("assignmentUrl").GetRawText(),
            lab.GetProperty("assignmentUrl").GetRawText());
        Assert.Equal(
            listItem.GetProperty("defenseRequired").GetRawText(),
            lab.GetProperty("defenseRequired").GetRawText());
    }
}
