using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-196 «GET /me/profile преподавателя: ProfileDto с groupName=null»
/// (happy_path, FR-020, P1).
///
/// given: сессия teacher — сид-учётка FR-004 (login/email/fullName известны из
///        сида: Seed__TeacherLogin по умолчанию 'teacher', email 'teacher@example.com',
///        ФИО сида 'Сидоров Семён Семёнович'; сидится всегда и идемпотентно,
///        независимо от Seed__DemoData); GroupId=null (у teacher всегда null);
///        access-cookie минтится харнесом с role='teacher' (ADR-022).
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
        // given: сид-преподаватель (role=teacher, GroupId=null); сессия teacher.
        var teacher = B14Harness.ResolveSeedTeacher(_factory);
        using var client = B14Harness.CreateSessionClient(_factory, teacher.Id, "teacher");

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
