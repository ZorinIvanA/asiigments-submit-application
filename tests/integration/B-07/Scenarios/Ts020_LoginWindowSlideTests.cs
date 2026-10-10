using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B07.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-020 «Матрица login: окно 60с скользит» (boundary, FR-004 + FR-007, P1).
///
/// given: 5-я метка неудач входа по ключу 'teacher|IP' записана в момент t0
///        (инжектируемые часы фикстуры).
/// when:  неудачный POST /auth/login {login:'teacher', password:неверный} в
///        момент t0+61с.
/// then:  401 'Неверный логин или пароль' и метка записана — не 429 (FR-007 AC
///        «Окно скользит»; FR-004 окно 60с).
///
/// Ключ записи наблюдается публичным швом состояния (IRateLimitStore.GetKeys):
/// состав ключа воспроизводится публичными LimiterKeys.FromLogin/FromIp с
/// разделителем '|' (IF-006/FR-004) — формат ключа login|IP контролируется
/// тестом, внутренняя склейка BuildKey не дублируется.
/// </summary>
public sealed class Ts020_LoginWindowSlideTests : IClassFixture<B07RateLimitWebAppFactory>
{
    private const string ClientIp = "10.0.7.2";
    private const string WrongPassword = "definitely-wrong-pass";

    private readonly B07RateLimitWebAppFactory _factory;

    public Ts020_LoginWindowSlideTests(B07RateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Failure61SecondsAfterFifthMark_401WithFreshMark()
    {
        // given: 5 неудачных входов teacher — 5 меток 'teacher|IP' в момент t0.
        using var client = B07AuthClients.CreateClientWithIp(_factory, ClientIp);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failure = await B07AuthClients.PostLoginAsync(client, "teacher", WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        var t0 = _factory.Time.GetUtcNow();

        // when: сдвиг инжектируемых часов на 61с и очередной неудачный вход.
        _factory.Time.Advance(TimeSpan.FromSeconds(61));
        using var afterWindow = await B07AuthClients.PostLoginAsync(client, "teacher", WrongPassword);

        // then: 401 'Неверный логин или пароль' — не 429.
        Assert.Equal(HttpStatusCode.Unauthorized, afterWindow.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(afterWindow);
        BodyAssertions.MessageIs(envelope, "Неверный логин или пароль");

        // then: свежая метка записана в ключ 'lower(trim(login))|IP': состав ключа
        // воспроизводится публичными LimiterKeys (trim/lower внутри), разделитель
        // '|' — IF-006/FR-004; внутренняя склейка BuildKey не дублируется.
        var store = _factory.Services.GetRequiredService<IRateLimitStore>();
        var key = Assert.Single(store.GetKeys(RateLimitPolicies.Login));
        Assert.Equal(
            LimiterKeys.FromLogin("teacher") + "|" + LimiterKeys.FromIp(ClientIp),
            key);
        Assert.True(store.TryGetMarks(RateLimitPolicies.Login, key, out var marks),
            "после скольжения окна неудачная попытка пишет свежую метку");
        Assert.Single(marks);
        Assert.True(marks[0] > t0.ToUnixTimeMilliseconds(), "записанная метка — момента t0+61с");
    }
}
