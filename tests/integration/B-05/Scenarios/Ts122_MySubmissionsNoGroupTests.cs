using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-122 «me/submissions: студент без группы» (happy_path, FR-021).
///
/// given: student31 без группы (демо-сид); его валидная сессия (минт, ADR-015).
/// when:  GET /api/v1/me/submissions?semester=1
/// then:  200 {hasGroup:false, labs:[], submissions:[]} (FR-021 AC «Свои сдачи
///        без группы»).
/// </summary>
public sealed class Ts122_MySubmissionsNoGroupTests : IClassFixture<B05WebAppFactory>
{
    private const string MySubmissionsEndpoint = "/api/v1/me/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts122_MySubmissionsNoGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task StudentWithoutGroup_GetsEmptyAnswerWithHasGroupFalse()
    {
        // given: student31 существует и не включён ни в одну группу.
        var student31 = B05SeedLookup.StudentByLogin(_factory, "student31");
        Assert.Null(student31.GroupId);

        using var client = B05MintedSessions.Create(_factory);
        B05MintedSessions.MintAccessCookie(_factory, client, student31.Id, UserRoles.Student);

        // when: свои сдачи по семестру 1.
        using var response = await client.GetAsync($"{MySubmissionsEndpoint}?semester=1");

        // then: 200 {hasGroup:false, labs:[], submissions:[]}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "hasGroup", "labs", "submissions");
        Assert.False(root.GetProperty("hasGroup").GetBoolean());
        Assert.Equal(0, root.GetProperty("labs").GetArrayLength());
        Assert.Equal(0, root.GetProperty("submissions").GetArrayLength());
    }
}
