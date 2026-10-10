using System.Text;
using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-021 «Матрица register: лимит 5/3600с по IP применяется ДО валидации
/// тела» (negative, FR-004 + FR-006, P0).
///
/// given: с IP X за текущий час выполнены 5 POST /auth/register с РАЗНЫМИ
///        свободными логинами reg01..reg05 и разными email (все — 201):
///        различие логинов исключает гипотетический ключ 'IP|login' —
///        исчерпание счётчика после 5 попыток возможно только при ключе,
///        состоящем из одного IP; пользователь с логином 'zzz' не существует.
/// when:  6-й POST /auth/register с IP X с телом '{bad json'.
/// then:  429 RATE_LIMITED 'Слишком много попыток. Повторите позже'; тело не
///        разбирается и не валидируется (нет 400 VALIDATION); пользователь
///        'zzz' не создан (FR-004 AC «Register: лимит до валидации»; ключ
///        матрицы — IP, без логина).
/// </summary>
public sealed class Ts021_RegisterLimitBeforeValidationTests : IClassFixture<B08LimitersRateLimitWebAppFactory>
{
    private const string ClientIp = "10.19.8.1";
    private const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    private readonly B08LimitersRateLimitWebAppFactory _factory;

    public Ts021_RegisterLimitBeforeValidationTests(B08LimitersRateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_SixthAttemptWithGarbageBody_429BeforeValidation()
    {
        // given: пользователь 'zzz' не существует.
        Assert.Null(B08LimitersClients.FindUserByLogin(_factory, "zzz"));

        // given: 5 успешных регистраций (201) с IP X — разные свободные логины
        // reg01..reg05 и разные email; ключ счётчика может состоять только из IP.
        using var client = B08LimitersClients.CreateClientWithIp(_factory, ClientIp);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var created = await client.PostAsJsonAsync(B08LimitersClients.RegisterEndpoint, new
            {
                fullName = $"Reg User {attempt:D2}",
                login = $"reg{attempt:D2}",
                email = $"reg{attempt:D2}@example.com",
                password = B08LimitersClients.TestUserPassword,
                repeatPassword = B08LimitersClients.TestUserPassword,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        // when: 6-й POST с IP X с телом '{bad json'.
        using var sixth = await client.PostAsync(
            B08LimitersClients.RegisterEndpoint,
            new StringContent("{bad json", Encoding.UTF8, "application/json"));

        // then: 429 RATE_LIMITED 'Слишком много попыток. Повторите позже' —
        // тело не разбирается и не валидируется (нет 400 VALIDATION).
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var envelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(sixth);
        B08LimitersBodyAssertions.MessageIs(envelope, RateLimitedMessage);

        // then: пользователь 'zzz' не создан.
        Assert.Null(B08LimitersClients.FindUserByLogin(_factory, "zzz"));
    }
}
