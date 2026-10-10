using LabsApp.IntegrationTests.B17.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Groups.Scenarios;

/// <summary>
/// TS-128 «Группы: границы имени 100/101» (boundary, FR-019, P2).
///
/// given: сессия teacher; имя свободно (хранилище без групп — только
///        сид-преподаватель).
/// when:  POST /groups с name длиной ровно 100; затем с name длиной 101.
/// then:  первый — 201; второй — 400, errors.name =
///        ['Название группы — от 1 до 100 символов'] (FR-019: «name 1–100 после
///        трима»).
/// </summary>
public sealed class Ts128_GroupNameLengthBoundaryTests : IClassFixture<B17GroupsWebAppFactory>
{
    private const string NameValidationText = "Название группы — от 1 до 100 символов";

    private readonly B17GroupsWebAppFactory _factory;

    public Ts128_GroupNameLengthBoundaryTests(B17GroupsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_NameOf100CharsCreated_NameOf101Rejected()
    {
        // given: сессия teacher; имена граничной длины без пробельных символов
        // (после трима длина сохраняется).
        using var client = B17GroupsSession.CreateTeacher(_factory);
        var name100 = new string('Г', 100);
        var name101 = new string('Г', 101);

        // when: POST /groups с name длиной ровно 100.
        using var created = await B17GroupsApi.PostGroupAsync(client, name100);

        // then: 201.
        using var createdBody = await B17GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:100 символов} (teacher)");

        // when: POST /groups с name длиной 101.
        using var rejected = await B17GroupsApi.PostGroupAsync(client, name101);

        // then: 400; errors.name = ['Название группы — от 1 до 100 символов'].
        using var rejectedBody = await B17GroupsApi.ParseWithStatusAsync(
            rejected, HttpStatusCode.BadRequest, "POST /groups {name:101 символ} (teacher)");
        B17GroupsApi.FieldErrorsExactly(rejectedBody.RootElement, "name", NameValidationText);
    }
}
