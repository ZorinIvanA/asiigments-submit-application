using LabsApp.Auth.RateLimiting;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-157 «Scope: X-Forwarded-For не влияет на ключ лимитера» (scope, FR-004 +
/// FR-007, P2).
///
/// given: по ключу 'victim|IP' уже 4 метки неудач (IP —
///        Connection.RemoteIpAddress тестового клиента, фиксирован);
///        пользователь 'victim' существует; во всех попытках пароль неверный.
/// when:  5-я неудачная попытка входа с login 'victim' с заголовком
///        X-Forwarded-For: 1.2.3.4; затем 6-я неудачная попытка с заголовком
///        X-Forwarded-For: 5.6.7.8.
/// then:  5-я — 401 'Неверный логин или пароль' (меток в ключе стало 5);
///        6-я — 429 'Слишком много попыток. Повторите позже': все попытки
///        учтены в ОДНОМ ключе по RemoteIpAddress — подмена заголовка не
///        разделяет и не сдвигает ключи (ASM-013; обработка XFF —
///        out_of_scope).
/// </summary>
public sealed class Ts157_XffDoesNotChangeLimiterKeyTests
{
    private const string Login = "victim";
    private const string Email = "victim@example.com";
    private const string WrongPassword = "Wrong0rd!";

    [Fact]
    public async Task FailedLoginsWithForgedXff_CountedInSingleRemoteIpKey()
    {
        using var factory = new B08AuthDevFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // given: пользователь 'victim' существует; 4 неудачных попытки без
        // заголовков дают 4 метки ключа 'victim|RemoteIpAddress'.
        B08AuthHost.SeedUser(factory, Login, Email, "Жертва Лимитера", UserRoles.Student);
        var wrongBody = "{\"login\":\"" + Login + "\",\"password\":\"" + WrongPassword + "\"}";
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var attemptResponse = await client.PostAsync(B08AuthHost.LoginEndpoint, wrongBody);
            B08AuthHost.AssertStatus(attemptResponse, HttpStatusCode.Unauthorized, $"предусловие: неудачная попытка {attempt} из 4 (TS-157)");
            Assert.Equal(B08AuthHost.WrongCredentialsMessage, await Message(attemptResponse));
        }

        // given: все метки — в ОДНОМ ключе (инспекция политики login лимитера).
        var limiter = factory.Services.GetRequiredService<LoginFailureLimiter>();
        Assert.Equal(1, limiter.TrackedKeysCount);

        // when: 5-я неудачная попытка с подменённым X-Forwarded-For: 1.2.3.4.
        using var fifth = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            wrongBody,
            header: ("X-Forwarded-For", "1.2.3.4"));

        // then: 401 'Неверный логин или пароль' (меток в ключе стало 5 — блок
        // ещё не достигнут; подмена заголовка не открыла новый ключ).
        B08AuthHost.AssertStatus(fifth, HttpStatusCode.Unauthorized, "5-я неудачная попытка с X-Forwarded-For: 1.2.3.4 (TS-157)");
        Assert.Equal(B08AuthHost.WrongCredentialsMessage, await Message(fifth));
        Assert.Equal(1, limiter.TrackedKeysCount);

        // when: 6-я неудачная попытка с другим заголовком X-Forwarded-For: 5.6.7.8.
        using var sixth = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            wrongBody,
            header: ("X-Forwarded-For", "5.6.7.8"));

        // then: 429 'Слишком много попыток. Повторите позже' — окно ключа
        // 'victim|RemoteIpAddress' исчерпано; XFF не разделил и не сдвинул ключи.
        B08AuthHost.AssertStatus(sixth, HttpStatusCode.TooManyRequests, "6-я неудачная попытка с X-Forwarded-For: 5.6.7.8 (TS-157)");
        Assert.Equal(B08AuthHost.RateLimitedMessage, await Message(sixth));
        Assert.Equal(1, limiter.TrackedKeysCount);
    }

    private static async Task<string?> Message(HttpResponseMessage response)
    {
        var body = await B08AuthHost.ReadJsonObjectAsync(response, "тело отказа входа (TS-157)");
        return B08AuthHost.StringProperty(body, "message");
    }
}
