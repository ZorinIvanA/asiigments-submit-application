using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-051 «Неизвестный логин: тот же текст и структура тела» (negative,
/// FR-013, P0).
///
/// given: логина 'ghost' в хранилище нет.
/// when:  POST /auth/login {login:'ghost', password:'whatever1!'}.
/// then:  401 с тем же текстом «Неверный логин или пароль» и той же структурой
///        тела, что у неверного пароля. FR-013 AC «Неизвестный логин».
/// </summary>
public sealed class Ts051_LoginUnknownLoginTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts051_LoginUnknownLoginTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UnknownLogin_SameStatusTextAndBodyShapeAsWrongPassword()
    {
        // given: teacher существует (эталон ветки «неверный пароль»), логина 'ghost'
        // в хранилище нет; свежая фикстура — неуспешных попыток в окне нет.
        using var client = B07AuthClients.CreateClient(_factory);

        // when: эталонный неуспех с неверным паролем; отдельно — неизвестный логин.
        using var wrongPassword = await B07AuthClients.PostLoginAsync(client, "teacher", "wrong1!");
        using var unknownLogin = await B07AuthClients.PostLoginAsync(client, "ghost", "whatever1!");

        // then: оба — 401 с одним текстом и одной структурой тела.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownLogin.StatusCode);

        var wrongBody = await BodyAssertions.ReadRootObjectAsync(wrongPassword);
        var unknownBody = await BodyAssertions.ReadRootObjectAsync(unknownLogin);
        BodyAssertions.MessageIs(wrongBody, "Неверный логин или пароль");
        BodyAssertions.MessageIs(unknownBody, "Неверный логин или пароль");

        var wrongKeys = BodyPropertyNames(wrongBody);
        var unknownKeys = BodyPropertyNames(unknownBody);
        Assert.True(
            wrongKeys.SequenceEqual(unknownKeys, StringComparer.Ordinal),
            $"Структура тел 401 различается: неверный пароль [{string.Join(", ", wrongKeys)}], " +
            $"неизвестный логин [{string.Join(", ", unknownKeys)}].");
    }

    private static string[] BodyPropertyNames(System.Text.Json.JsonElement body) =>
        body.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
}
