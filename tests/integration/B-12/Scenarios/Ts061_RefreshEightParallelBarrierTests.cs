using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-061 (P1, idempotency; FR-009) «Refresh: параллельные запросы идемпотентны
/// без ротации».
/// given: Валидная refresh-cookie; N=8 параллельных запросов со стартовой барьерой.
/// when:  8 одновременных POST /auth/refresh с одним и тем же refresh-токеном;
///        затем ещё один refresh тем же токеном.
/// then:  Все 8 — 204; исходный refresh-токен остаётся действительным (финальный
///        refresh — 204; ротации нет) (FR-009: «Параллельные refresh-запросы
///        допустимы и все получают 204»).
/// Стартовая барьера — DelegatingHandler в конвейере клиента: каждый из 8 запросов
/// удерживается, пока все 8 не дойдут до барьеры; открывший её восьмой участник
/// выпускает все 8 на сервер практически одновременно. Сиквенс-номер кейса волны
/// прежней нумерации — TS-057 (файл Ts057_RefreshParallelRequestsIdempotentTests,
/// N=10, инспекция хранилища); этот файл замыкает кейс дословно по given/when/then.
/// </summary>
public sealed class Ts061_RefreshEightParallelBarrierTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    /// <summary>N кейса: 8 одновременных refresh-запросов.</summary>
    private const int Parties = 8;

    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task EightParallelRefreshes_ThenFinalRefreshWithSameToken_AllNoContent_NoRotation()
    {
        // given: валидная refresh-cookie (DI-минт, ADR-015: без POST /auth/login).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var refreshTokenValue = B12AuthSessions.CreateLiveRefreshToken(_factory, teacher.Id);

        using var client = CreateBarrieredClient(Parties);

        // when: 8 одновременных POST /auth/refresh с одним и тем же refresh-токеном
        // (стартовая барьера: ни один запрос не уходит, пока не готовы все 8);
        // отдельное сообщение на запрос, исход исключения любого из них уронил бы
        // Task.WhenAll.
        var tasks = new List<Task<HttpResponseMessage>>(Parties);
        for (var i = 0; i < Parties; i++)
        {
            tasks.Add(client.SendAsync(B12AuthSessions.CreateRefreshRequest(refreshTokenValue)));
        }

        var responses = await Task.WhenAll(tasks);
        try
        {
            // then: все 8 — 204; ротации нет (Set-Cookie refresh_token отсутствует).
            Assert.All(responses, response =>
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.False(
                    B12AuthSessions.TryGetSetCookie(
                        response, AuthCoreDefaults.RefreshTokenCookieName, out _),
                    "Ротации нет: Set-Cookie refresh_token при refresh не допускается.");
            });
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // then: исходный refresh-токен остаётся действительным — ещё один refresh
        // тем же токеном — 204 (ротации нет: refresh_token не переустанавливается).
        using var finalResponse = await client.SendAsync(
            B12AuthSessions.CreateRefreshRequest(refreshTokenValue));
        Assert.Equal(HttpStatusCode.NoContent, finalResponse.StatusCode);
        Assert.False(
            B12AuthSessions.TryGetSetCookie(
                finalResponse, AuthCoreDefaults.RefreshTokenCookieName, out _),
            "Ротации нет: финальный refresh не переустанавливает refresh_token.");
    }

    /// <summary>
    /// Клиент над TestServer фабрики с конвейером «стартовая барьера → сервер»:
    /// первые N−1 запросов ждут открытия, N-й открывает барьеру и все уходят вместе.
    /// </summary>
    private HttpClient CreateBarrieredClient(int parties)
    {
        var barrier = new StartBarrierHandler(parties)
        {
            InnerHandler = _factory.Server.CreateHandler(),
        };
        return new HttpClient(barrier, disposeHandler: false)
        {
            BaseAddress = new Uri("http://localhost/"),
        };
    }

    /// <summary>
    /// Стартовая барьера given «N=8 параллельных запросов со стартовой барьерой»:
    /// каждый запрос задерживается в конвейере клиента, пока барьеру не откроет
    /// последний (N-й) участник.
    /// </summary>
    private sealed class StartBarrierHandler(int parties) : DelegatingHandler
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int remaining = parties;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                _gate.TrySetResult();
            }

            await _gate.Task.WaitAsync(cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
