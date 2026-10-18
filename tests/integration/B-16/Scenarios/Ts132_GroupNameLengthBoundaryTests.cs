using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-132 «Группы: граница длины имени 100/101» (boundary, FR-019, P1).
///
/// given: сессия teacher; уникальные имена длиной 100 и 101 символов (без
///        пробельных символов — длина сохраняется после трима; в хранилище таких
///        имён нет).
/// when:  два POST /api/v1/groups.
/// then:  имя длиной 100 — 201; имя длиной 101 — 400 с
///        errors.name=['Название группы — от 1 до 100 символов'] (FR-019).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts132_GroupNameLengthBoundaryTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string NameValidationText = "Название группы — от 1 до 100 символов";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts132_GroupNameLengthBoundaryTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_NameOf100CharsCreated_NameOf101Rejected()
    {
        // given: сессия teacher; уникальные имена длиной ровно 100 и 101.
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var name100 = new string('Д', 100);
        var name101 = new string('Д', 101);
        Assert.Equal(100, name100.Length);
        Assert.Equal(101, name101.Length);
        Assert.NotEqual(
            name100.ToLowerInvariant(),
            name101.ToLowerInvariant(),
            StringComparer.Ordinal);

        // when: POST /groups с именем длиной 100.
        using var created = await B16GroupsApi.PostGroupAsync(client, name100);

        // then: 201, имя без изменений.
        using var createdBody = await B16GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:100 символов} (teacher)");
        Assert.Equal(name100, B16GroupsApi.ReadString(createdBody.RootElement, "name"));

        // when: POST /groups с именем длиной 101.
        using var rejected = await B16GroupsApi.PostGroupAsync(client, name101);

        // then: 400 errors.name=['Название группы — от 1 до 100 символов'].
        using var rejectedBody = await B16GroupsApi.ParseWithStatusAsync(
            rejected, HttpStatusCode.BadRequest, "POST /groups {name:101 символ} (teacher)");
        B16GroupsApi.FieldErrorsExactly(rejectedBody.RootElement, "name", NameValidationText);
    }
}
