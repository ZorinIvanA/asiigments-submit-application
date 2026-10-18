using System.Text;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-021 «Матрица register: лимит 5/3600с по IP применяется ДО валидации тела»
/// (negative, FR-004 + FR-006, P0).
///
/// given: с IP X за текущий час выполнено 5 POST /auth/register с РАЗНЫМИ
///        свободными логинами reg01..reg05 и разными email (все — 201):
///        различие логинов исключает гипотетический ключ 'IP|login' —
///        исчерпание счётчика после 5 попыток возможно только при ключе,
///        состоящем из одного IP; пользователь с логином 'zzz' не существует.
/// when:  6-й POST /auth/register с IP X с телом '{bad json'.
/// then:  429 RATE_LIMITED 'Слишком много попыток. Повторите позже'; тело не
///        разбирается и не валидируется (нет 400 VALIDATION); пользователь 'zzz'
///        не создан (FR-004 AC «Register: лимит до валидации»; ключ матрицы —
///        IP, без логина).
/// </summary>
public sealed class Ts021_RegisterLimitBeforeValidationTests : IClassFixture<B07RateLimitWebAppFactory>
{
    private const string ClientIp = "10.0.8.1";

    private readonly B07RateLimitWebAppFactory _factory;

    public Ts021_RegisterLimitBeforeValidationTests(B07RateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_SixthAttemptWithGarbageBody_429BeforeValidation()
    {
        // given: 5 успешных регистраций (201) с IP X — разные свободные логины
        // reg01..reg05 и разные email; ключ счётчика может состоять только из IP.
        using var client = B07AuthClients.CreateClientWithIp(_factory, ClientIp);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var created = await client.PostAsJsonAsync(B07AuthClients.RegisterEndpoint, new
            {
                fullName = $"Reg User {attempt:D2}",
                login = $"reg{attempt:D2}",
                email = $"reg{attempt:D2}@example.com",
                password = HostClients.TestUserPassword,
                repeatPassword = HostClients.TestUserPassword,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        // when: 6-й POST с телом '{bad json'.
        using var sixth = await client.PostAsync(
            B07AuthClients.RegisterEndpoint,
            new StringContent("{bad json", Encoding.UTF8, "application/json"));

        // then: 429 RATE_LIMITED — тело не разбирается и не валидируется
        // (400 VALIDATION не возвращается).
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(envelope, "Слишком много попыток. Повторите позже");

        // then: пользователь 'zzz' не создан.
        Assert.Null(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin("zzz"));
    }
}
