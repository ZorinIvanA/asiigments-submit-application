using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-050 «Неверный пароль: единый 401 без раскрытия» (negative, FR-013, P0).
///
/// given: пользователь teacher существует.
/// when:  POST /auth/login {login:'teacher', password:'wrong1!'}.
/// then:  401; тело {"message":"Неверный логин или пароль"}. FR-013 AC
///        «Неверный пароль».
/// </summary>
public sealed class Ts050_LoginWrongPasswordTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts050_LoginWrongPasswordTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task WrongPassword_ReturnsUnified401Body()
    {
        // given: пользователь teacher существует (сид фикстуры); свежая фикстура —
        // неуспешных попыток в окне нет.
        using var client = B07AuthClients.CreateClient(_factory);

        // when: POST /auth/login {login:'teacher', password:'wrong1!'}.
        using var response = await B07AuthClients.PostLoginAsync(client, "teacher", "wrong1!");

        // then: 401; тело {"message":"Неверный логин или пароль"} (без errors).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Неверный логин или пароль");
        Assert.False(
            root.TryGetProperty("errors", out _),
            "Тело 401 входа не должно содержать errors: структура {\"message\":\"…\"}.");
    }
}
