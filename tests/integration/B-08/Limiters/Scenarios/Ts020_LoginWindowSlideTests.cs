using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-020 «Матрица login: окно 60с скользит; ключ наблюдается через публичный
/// шов хранилища» (boundary, FR-004 + FR-007, P0).
///
/// given: хост Development с инжектируемыми часами (FakeTimeProvider.SetUtcNow)
///        и фиксированным IP тестового клиента; в момент T0 выполнены 5
///        неудачных POST /auth/login с login 'teacher' (неверный пароль) —
///        5 меток в ключе login-политики. Состав ключа наблюдается публичным
///        швом: IRateLimitStore.GetKeys(RateLimitPolicies.Login) содержит ключ,
///        равный LimiterKeys.FromLogin('teacher')+'|'+LimiterKeys.FromIp(IP) —
///        трим/нижний регистр внутри LimiterKeys, разделитель '|' (IF-006);
///        при проверке состав ключа воспроизводится вызовами публичных
///        LimiterKeys, а не дублированием внутренней склейки. Семантика
///        снимка: TryGetMarks возвращает метки без вычистки — чтение ПОСЛЕ
///        запроса, вычистку уже выполнил движок.
/// when:  часы переведены на T0+61с; POST /auth/login {login:'teacher',
///        password:неверный}.
/// then:  401 'Неверный логин или пароль', НЕ 429 — метки момента T0 вычищены
///        (окно 60с); новая метка записана в тот же ключ: TryGetMarks(Login,
///        ключ, out marks) после запроса содержит ровно 1 метку момента T0+61с
///        (FR-007 AC «Окно скользит»; FR-004 окно 60с).
/// </summary>
public sealed class Ts020_LoginWindowSlideTests : IClassFixture<B08LimitersRateLimitWebAppFactory>
{
    private const string ClientIp = "10.19.7.2";
    private const string WrongPassword = "definitely-wrong-pass";

    private readonly B08LimitersRateLimitWebAppFactory _factory;

    public Ts020_LoginWindowSlideTests(B08LimitersRateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Failure61SecondsAfterFifthMark_401WithFreshMark()
    {
        // given: 5 неудачных входов teacher — 5 меток 'teacher|IP' в момент T0.
        using var client = B08LimitersClients.CreateClientWithIp(_factory, ClientIp);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failure = await B08LimitersClients.PostLoginAsync(client, "teacher", WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        // Состав ключа — публичный шов LimiterKeys + разделитель '|' (IF-006);
        // внутренняя склейка BuildKey не дублируется.
        var expectedKey = LimiterKeys.FromLogin("teacher") + "|" + LimiterKeys.FromIp(ClientIp);
        var store = _factory.Services.GetRequiredService<IRateLimitStore>();
        Assert.Contains(store.GetKeys(RateLimitPolicies.Login), key => key == expectedKey);
        var t0 = _factory.Time.GetUtcNow();

        // when: часы переведены на T0+61с (SetUtcNow); неудачный вход 'teacher'.
        _factory.Time.SetUtcNow(t0.AddSeconds(61));
        using var afterWindow = await B08LimitersClients.PostLoginAsync(client, "teacher", WrongPassword);

        // then: 401 'Неверный логин или пароль', НЕ 429 — метки момента T0
        // вычищены (окно 60с скользит).
        Assert.Equal(HttpStatusCode.Unauthorized, afterWindow.StatusCode);
        var envelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(afterWindow);
        B08LimitersBodyAssertions.MessageIs(envelope, "Неверный логин или пароль");

        // then: новая метка записана в тот же ключ: ровно 1 метка момента T0+61с
        // (чтение ПОСЛЕ запроса — вычистку выполнил движок при проверке).
        Assert.True(
            store.TryGetMarks(RateLimitPolicies.Login, expectedKey, out var marks),
            "после скольжения окна неудачная попытка пишет свежую метку в тот же ключ");
        Assert.Single(marks);
        Assert.Equal(_factory.Time.GetUtcNow().ToUnixTimeMilliseconds(), marks[0]);
    }
}
