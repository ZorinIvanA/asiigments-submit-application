using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-046 (P1, boundary; FR-007) «Вход: логин без учёта регистра и с пробелами»
/// (актуальная нумерация кейсов батча B-10; родственный тест предыдущей нумерации
/// — первая часть Ts045_LoginTrimCaseAndMissingFieldsTests).
/// given: teacher/teacher123! существует; меток лимитера нет.
/// when:  POST /auth/login {login:'  TEACHER  ', password:'teacher123!'}.
/// then:  200 MeDto (логин триммится и ищется по lower(login)) (AC FR-007
///        «Логин без учёта регистра и с пробелами»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts046_LoginTrimAndCaseInsensitiveTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.46";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS046_Login_WithPaddedUpperCaseLogin_Returns200MeDto()
    {
        // given: teacher/teacher123! существует (сид); меток лимитера нет (свежий хост).
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST /auth/login {login:'  TEACHER  ', password:'teacher123!'}.
        using var response = await B10AuthRequests.LoginAsync(client, "  TEACHER  ", B10AuthRequests.TeacherPassword);
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 200 MeDto — логин триммится и ищется по lower(login).
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());

        // then: ветка найденного пользователя — ровно одна деривация Verify (Δkdf=1).
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
    }
}
