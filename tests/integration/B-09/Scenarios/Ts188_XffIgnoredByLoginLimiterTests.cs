using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-188 «Scope: X-Forwarded-For игнорируется лимитером» (scope,
/// OUT-SCOPE-XFF + FR-004, P2).
///
/// given: 5 неудачных входов по login 'teacher' с одного соединения; в каждом
///        запросе заголовок X-Forwarded-For принимает разные значения.
/// when:  6-я неудачная попытка с вновь другим X-Forwarded-For.
/// then:  429 — ключ лимитера строится по Connection.RemoteIpAddress,
///        заголовок не влияет (ASM-013; out_of_scope: «Обработка
///        X-Forwarded-For»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// с совпадающим поведением (Ts157_XffDoesNotChangeLimiterKey) не изменялся.
/// </summary>
public sealed class Ts188_XffIgnoredByLoginLimiterTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string Login = "teacher";
    private const string WrongPassword = "Wrong0rd!";
    private const string ClientIp = "10.0.0.188";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task SixthFailedLoginWithNewXff_IsRateLimited_InSingleRemoteIpKey()
    {
        // given: учитель существует (сид фикстуры); один клиент — одно
        // соединение с фиксированным Connection.RemoteIpAddress.
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, ClientIp);
        var limiter = _factory.Services.GetRequiredService<LoginFailureLimiter>();

        // given: 5 неудачных входов по login 'teacher'; в КАЖДОМ запросе
        // заголовок X-Forwarded-For принимает разные значения.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await PostLoginWithXff(client, $"203.0.113.{attempt}");
            var body = await B09Assertions.ParseObjectAsync(
                response, HttpStatusCode.Unauthorized, $"неудачная попытка {attempt} из 5 (TS-188)");
            B09Assertions.MessageIs(body, B09AuthSupport.WrongCredentialsMessage);
        }

        // then (предусловие): все 5 попыток учтены в ОДНОМ ключе
        // 'teacher|RemoteIpAddress' — заголовки не разделили ключи.
        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, Login, ClientIp));
        Assert.Equal(1, limiter.TrackedKeysCount);

        // when: 6-я неудачная попытка с вновь другим X-Forwarded-For.
        using var sixth = await PostLoginWithXff(client, "203.0.113.200");

        // then: 429 'Слишком много попыток. Повторите позже' — окно ключа
        // 'teacher|RemoteIpAddress' исчерпано; XFF не влияет на ключ (метку
        // отказ не дописывает).
        var sixthBody = await B09Assertions.ParseObjectAsync(
            sixth, HttpStatusCode.TooManyRequests, "6-я неудачная попытка с новым X-Forwarded-For (TS-188)");
        B09Assertions.MessageIs(sixthBody, B09AuthSupport.RateLimitedMessage);
        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, Login, ClientIp));
        Assert.Equal(1, limiter.TrackedKeysCount);
    }

    /// <summary>POST /auth/login с неверным паролем и заголовком X-Forwarded-For.</summary>
    private static Task<HttpResponseMessage> PostLoginWithXff(HttpClient client, string xffValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, B09AuthSupport.LoginEndpoint)
        {
            Content = new StringContent(
                "{\"login\":\"" + Login + "\",\"password\":\"" + WrongPassword + "\"}",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("X-Forwarded-For", xffValue);
        return client.SendAsync(request);
    }
}
