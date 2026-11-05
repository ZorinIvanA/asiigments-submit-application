using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-122 «me/submissions: пользователь без группы» (happy_path, FR-021).
///
/// given: текущий пользователь анонимного приложения — сид-преподаватель
///        (Seed__TeacherLogin); он не включён ни в одну группу (GroupId=null).
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
    public async Task CurrentUserWithoutGroup_GetsEmptyAnswerWithHasGroupFalse()
    {
        // given: сид-преподаватель существует и не включён ни в одну группу.
        var teacher = await B05SeedLookup.TeacherAsync(_factory);
        Assert.Null(teacher.GroupId);

        using var client = HostClients.Create(_factory);

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
