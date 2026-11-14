using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-041 (P0, negative; FR-007, FR-004) «Вход: неверный пароль известного логина —
/// 401, метка записана» (актуальная нумерация кейсов батча B-10; родственный тест
/// предыдущей нумерации — Ts040_LoginWrongPasswordMarkWrittenTests).
/// given: teacher существует; меток нет; счётчик обнулён.
/// when:  POST /auth/login {login:'teacher', password:'nope123!'}.
/// then:  401, message 'Неверный логин или пароль'; Δkdf=1; метка по ключу
///        'teacher|IP' записана (AC FR-007 «Неверный пароль, известный логин»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts041_LoginWrongPasswordMarkWrittenTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.41";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS041_Login_WrongPasswordOfKnownLogin_Returns401DeltaKdf1AndWritesMark()
    {
        // given: teacher существует; меток нет (свежий хост); счётчик обнулён.
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST /auth/login {login:'teacher', password:'nope123!'}.
        using var response = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 401, message 'Неверный логин или пароль'; Δkdf=1.
        _ = await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Неверный логин или пароль");
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");

        // then: метка по ключу 'teacher|IP' записана (учтённая неудачная попытка).
        Assert.Equal(1, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));
    }
}
