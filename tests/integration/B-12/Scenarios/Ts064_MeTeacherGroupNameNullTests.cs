using System.Text.Json;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-064 (P1, happy_path; FR-011) «auth/me: преподаватель — groupName null».
/// given: Валидная сессия преподавателя (сид).
/// when:  GET /auth/me с access учителя.
/// then:  200; role='teacher'; groupName=null (FR-011 AC «Преподаватель»).
/// </summary>
public sealed class Ts064_MeTeacherGroupNameNullTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task Me_ForTeacher_ReturnsNullGroupName()
    {
        // given: валидная сессия преподавателя (сид развёрнут при построении хоста).
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /auth/me с access учителя.
        using var response = await client.GetAsync(B12AuthEndpoints.Me);

        // then: 200; role='teacher'; groupName=null (в JSON присутствует как null).
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal("teacher", root.GetProperty("role").GetString());
        var groupName = root.GetProperty("groupName");
        Assert.Equal(JsonValueKind.Null, groupName.ValueKind);
    }
}
