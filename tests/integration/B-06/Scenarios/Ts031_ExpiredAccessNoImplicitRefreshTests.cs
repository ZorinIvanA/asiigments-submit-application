using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-031 «Просроченный access при действующем refresh: 401, неявного обновления нет»
/// (негативный, P0, FR-011 AC «Просроченный access»).
///
/// given: вход выполнен; FakeTimeProvider переведён на Auth__AccessTtlMinutes+1 минуту
///        (exp access в прошлом); refresh-запись ещё действительна (TTL дней —
///        7 дней >> 16 минут, действительность следует из арифметики TTL).
/// when:  GET /api/v1/auth/me с прежней access-cookie.
/// then:  HTTP 401 «Не авторизован»; неявного обновления нет — ответ не устанавливает
///        новой access/refresh cookie («обновление только явным POST /auth/refresh»).
/// </summary>
public sealed class Ts031_ExpiredAccessNoImplicitRefreshTests : IClassFixture<B06FakeTimeWebAppFactory>
{
    private readonly B06FakeTimeWebAppFactory _factory;

    public Ts031_ExpiredAccessNoImplicitRefreshTests(B06FakeTimeWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMeWithExpiredAccessCookie_Returns401WithoutImplicitRenewal()
    {
        // given: вход выполнен (обе cookie в контейнере клиента, refresh-запись создана).
        using var client = HostClients.Create(_factory);
        using var login = await ApiRequests.LoginAsync(
            client,
            SeedOptions.DefaultTeacherLogin,
            B06FakeTimeWebAppFactory.TestTeacherPassword);
        Assert.True(
            login.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход должен вернуть 200, фактически {login.StatusCode}: {await login.Content.ReadAsStringAsync()}");

        // given: перевод времени на Auth__AccessTtlMinutes+1 минуту — exp access в прошлом.
        var accessTtlMinutes = _factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.AccessTtlMinutes;
        _factory.Time.Advance(TimeSpan.FromMinutes(accessTtlMinutes + 1));

        // when: GET /api/v1/auth/me с прежней access-cookie (refresh также передаётся —
        // Path=/api/v1/auth совпадает, и он ещё действителен).
        using var response = await client.GetAsync(ApiRequests.MeEndpoint);

        // then: HTTP 401 «Не авторизован»; неявного обновления нет.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Не авторизован");
        CookieAssertions.AssertNoNewAuthCookie(response);
    }
}
