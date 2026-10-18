using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-085 «Профиль: GET студента с группой — полный ProfileDto»
/// (happy_path, FR-015, P0; нумерация текущего батча B-16).
///
/// given: student01 авторизован и состоит в ИК-221 (DI-сид группы и пользователя
///        с дословно кейсовыми значениями; сессия — минтованный access-cookie,
///        ADR-015).
/// when:  GET /api/v1/me/profile под student01.
/// then:  200 ProfileDto {login:'student01', email:'student01@example.com',
///        fullName:'Иванов Иван Иванович 01', role:'student',
///        groupName:'ИК-221'} — поле в поле DTO клиента (AC FR-015 «GET профиля
///        студента с группой»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts085_ProfileGetFullProfileDtoTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Иванов Иван Иванович 01";
    private const string GroupName = "ИК-221";

    private readonly B16WebAppFactory _factory;

    public Ts085_ProfileGetFullProfileDtoTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_StudentInGroup_ReturnsEveryDtoField()
    {
        // given: student01 авторизован и состоит в ИК-221 (DI-сид, сессия минтована).
        var group = B16Harness.SeedGroup(_factory, GroupName);
        var student = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: group.Id,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, student.Id, UserRoles.Student);

        // when: GET /api/v1/me/profile под student01.
        using var response = await client.GetAsync(B16Harness.ProfileEndpoint);

        // then: 200 ProfileDto — поле в поле (AC FR-015 «GET профиля студента с группой»).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "login", "student01");
        B16Assertions.StringPropertyIs(root, "email", "student01@example.com");
        B16Assertions.StringPropertyIs(root, "fullName", "Иванов Иван Иванович 01");
        B16Assertions.StringPropertyIs(root, "role", "student");
        B16Assertions.StringPropertyIs(root, "groupName", "ИК-221");
    }
}
