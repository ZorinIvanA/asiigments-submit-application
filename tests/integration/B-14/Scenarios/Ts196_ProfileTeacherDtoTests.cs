using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-196 «GET /me/profile: ProfileDto сид-преподавателя с groupName=null»
/// (happy_path, FR-020, P1).
///
/// given: анонимный клиент; текущий пользователь — сид-учётка FR-004
///        (login/email/fullName известны из сида: Seed__TeacherLogin по умолчанию
///        'teacher', email 'teacher@example.com', ФИО сида 'Сидоров Семён
///        Семёнович'; сидится всегда и идемпотентно, независимо от Seed__DemoData);
///        GroupId=null (у преподавателя всегда null).
/// when:  GET /api/v1/me/profile.
/// then:  200 ProfileDto {login, email, fullName, role:'teacher', groupName:null}.
///        FR-020: «ProfileDto … текущего пользователя (groupName вычисляется по
///        текущему состоянию групп)»; User.groupId constraints: «у teacher всегда null».
/// </summary>
public sealed class Ts196_ProfileTeacherDtoTests : IClassFixture<B14WebAppFactory>
{
    /// <summary>ФИО сид-преподавателя (SeedRunner: воспроизведение seed.ts, FR-004).</summary>
    private const string SeedTeacherFullName = "Сидоров Семён Семёнович";

    /// <summary>Email сид-преподавателя (SeedRunner: {login}@example.com).</summary>
    private const string SeedTeacherEmail = "teacher@example.com";

    private readonly B14WebAppFactory _factory;

    public Ts196_ProfileTeacherDtoTests(B14WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileOfSeedTeacher_ReturnsProfileDtoWithNullGroupName()
    {
        // given: текущий пользователь — сид-преподаватель (role=teacher, GroupId=null);
        // анонимный клиент.
        await B14Harness.ResolveSeedTeacherAsync(_factory);
        using var client = B14Harness.Create(_factory);

        // when: GET /api/v1/me/profile.
        using var response = await client.GetAsync(B14Harness.ProfileEndpoint);

        // then: 200 ProfileDto с данными сида, role='teacher' и groupName=null.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B14Assertions.ReadRootObjectAsync(response);
        B14Assertions.StringPropertyIs(root, "login", "teacher");
        B14Assertions.StringPropertyIs(root, "email", SeedTeacherEmail);
        B14Assertions.StringPropertyIs(root, "fullName", SeedTeacherFullName);
        B14Assertions.StringPropertyIs(root, "role", "teacher");
        B14Assertions.PropertyIsNull(root, "groupName");
    }
}
