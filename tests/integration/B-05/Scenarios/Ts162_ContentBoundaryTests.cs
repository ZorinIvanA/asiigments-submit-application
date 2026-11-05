using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-162 «Граница длины content работы: ровно 500 — 201, 501 — 400»
/// (boundary, FR-014).
///
/// given: teacher авторизован; пара (5,1) свободна (демо-сид содержит только
///        семестры 1 и 2).
/// when:  POST /api/v1/labs {number:1, semester:5, content: строка ровно 500
///        символов (без краевых пробелов), assignmentUrl:null,
///        defenseRequired:false}; затем с content ровно 501 символ.
/// then:  первый — HTTP 201 (граница 500 включительно, content в ответе не
///        искажён); второй — HTTP 400 «Данные заполнены неверно» с непустым
///        errors.content. FR-014: «content — 1–500 символов после трима».
/// </summary>
public sealed class Ts162_ContentBoundaryTests : IClassFixture<B05WebAppFactory>
{
    private const string LabsEndpoint = "/api/v1/labs";

    private readonly B05WebAppFactory _factory;

    public Ts162_ContentBoundaryTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Content_Exactly500Created_Exactly501Rejected()
    {
        // given: teacher авторизован; пара (5,1) свободна.
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        // when: content ровно 500 символов без краевых пробелов.
        var content500 = new string('Ж', 500);
        using var created = await client.PostAsJsonAsync(LabsEndpoint, new
        {
            number = 1,
            semester = 5,
            content = content500,
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 201; content в ответе не искажён (500 символов дословно).
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await BodyAssertions.ReadRootObjectAsync(created);
        Assert.True(
            string.Equals(createdBody.GetProperty("content").GetString(), content500, StringComparison.Ordinal),
            "Ожидался content ровно 500 символов в ответе без искажений.");
        Assert.Equal(500, createdBody.GetProperty("content").GetString()!.Length);

        // when: content ровно 501 символ.
        var content501 = new string('Ж', 501);
        using var rejected = await client.PostAsJsonAsync(LabsEndpoint, new
        {
            number = 2,
            semester = 5,
            content = content501,
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 400 «Данные заполнены неверно» с непустым errors.content.
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsNonEmptyStringArray(root, "content");
    }
}
