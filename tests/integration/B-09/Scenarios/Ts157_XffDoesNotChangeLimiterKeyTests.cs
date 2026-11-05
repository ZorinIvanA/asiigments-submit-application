using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-157 «Scope: X-Forwarded-For не влияет на ключ лимитера» (scope, FR-004 +
/// FR-007, P2).
///
/// given: по ключу 'victim|IP' уже 4 метки неудач (IP — Connection.RemoteIpAddress
///        тестового клиента, фиксирован); пользователь 'victim' существует; во
///        всех попытках пароль неверный.
/// when:  5-я неудачная попытка входа с login 'victim' с заголовком
///        X-Forwarded-For: 1.2.3.4; затем 6-я неудачная попытка с заголовком
///        X-Forwarded-For: 5.6.7.8.
/// then:  5-я — 401 'Неверный логин или пароль' (меток в ключе стало 5); 6-я —
///        429 'Слишком много попыток. Повторите позже': все попытки учтены в
///        ОДНОМ ключе по RemoteIpAddress — подмена заголовка не разделяет и не
///        сдвигает ключи (ASM-013; обработка XFF — out_of_scope).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (староволновые Ts157_ForbiddenBeforeNotFoundAndValidation
/// в B-12 и Ts169_TrustedProxyXff в B-05 помечены арбитражем дубликатами).
/// Поведенческая часть кейса исполнима дословно и исполнена в собственной зоне
/// батча B-09 (прецедент c-1052); расхождение размещения зафиксировано в
/// scenario_change_requests.
/// </summary>
public sealed class Ts157_XffDoesNotChangeLimiterKeyTests : IClassFixture<B09WebAppFactory>
{
    private const string Login = "victim09";
    private const string Email = "victim09@example.com";
    private const string RegistrationIp = "10.0.0.158";
    private const string ClientIp = "10.0.0.157";
    private const string WrongPassword = "Wrong0rd!";

    private readonly B09WebAppFactory _factory;

    public Ts157_XffDoesNotChangeLimiterKeyTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FailedLoginsWithForgedXff_CountedInSingleRemoteIpKey()
    {
        // given: пользователь 'victim' существует; 4 неудачных попытки без
        // заголовков дают 4 метки ключа 'victim|RemoteIpAddress'.
        await B09HostClients.RegisterStudentAsync(
            _factory, "Жертва Лимитера Девять", Login, Email, ip: RegistrationIp);

        using var client = B09AuthHttp.Create(_factory, ClientIp);
        var limiter = _factory.Services.GetRequiredService<LoginFailureLimiter>();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var attemptResponse = await B09AuthHttp.PostJsonAsync(
                client, B09AuthSupport.LoginEndpoint,
                "{\"login\":\"" + Login + "\",\"password\":\"" + WrongPassword + "\"}");
            B09AuthSupport.AssertStatus(
                attemptResponse, HttpStatusCode.Unauthorized, $"предусловие: неудачная попытка {attempt} из 4 (TS-157)");
            var body = await B09Assertions.ParseObjectAsync(attemptResponse, HttpStatusCode.Unauthorized, $"попытка {attempt}");
            B09Assertions.MessageIs(body, B09AuthSupport.WrongCredentialsMessage);
        }

        Assert.Equal(4, B09LoginMarkStore.MarksCount(_factory, Login, ClientIp));
        Assert.Equal(1, limiter.TrackedKeysCount);

        // when: 5-я неудачная попытка с подменённым X-Forwarded-For: 1.2.3.4.
        using var fifth = await PostLoginWithHeader(
            client, ("X-Forwarded-For", "1.2.3.4"));

        // then: 401 'Неверный логин или пароль' (меток в ключе стало 5 — блок
        // ещё не достигнут; подмена заголовка не открыла новый ключ).
        B09AuthSupport.AssertStatus(fifth, HttpStatusCode.Unauthorized, "5-я неудачная попытка с X-Forwarded-For: 1.2.3.4 (TS-157)");
        var fifthBody = await B09Assertions.ParseObjectAsync(fifth, HttpStatusCode.Unauthorized, "тело 5-й попытки");
        B09Assertions.MessageIs(fifthBody, B09AuthSupport.WrongCredentialsMessage);
        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, Login, ClientIp));
        Assert.Equal(1, limiter.TrackedKeysCount);

        // when: 6-я неудачная попытка с другим заголовком X-Forwarded-For: 5.6.7.8.
        using var sixth = await PostLoginWithHeader(
            client, ("X-Forwarded-For", "5.6.7.8"));

        // then: 429 'Слишком много попыток. Повторите позже' — окно ключа
        // 'victim|RemoteIpAddress' исчерпано; XFF не разделил и не сдвинул ключи
        // (429 метку НЕ дописывает).
        B09AuthSupport.AssertStatus(sixth, HttpStatusCode.TooManyRequests, "6-я неудачная попытка с X-Forwarded-For: 5.6.7.8 (TS-157)");
        var sixthBody = await B09Assertions.ParseObjectAsync(sixth, HttpStatusCode.TooManyRequests, "тело 6-й попытки");
        B09Assertions.MessageIs(sixthBody, B09AuthSupport.RateLimitedMessage);
        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, Login, ClientIp));
        Assert.Equal(1, limiter.TrackedKeysCount);
    }

    /// <summary>POST /auth/login с неверным паролем и дополнительным заголовком запроса.</summary>
    private static Task<HttpResponseMessage> PostLoginWithHeader(
        HttpClient client,
        (string Name, string Value) header)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, B09AuthSupport.LoginEndpoint)
        {
            Content = new StringContent(
                "{\"login\":\"" + Login + "\",\"password\":\"" + WrongPassword + "\"}",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add(header.Name, header.Value);
        return client.SendAsync(request);
    }
}
