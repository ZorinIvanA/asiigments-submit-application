using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-164 (P0, idempotency; FR-080) «Скользящее окно: после 61 с лимит входа
/// освобождается».
///
/// given: FakeTimeProvider на T0; лимит входа исчерпан (5 неуспешных входов
///        паролем за минуту по ключу (teacher, 10.0.0.1) — сид-преподаватель
///        харнеса с паролем фикстуры).
/// when:  TimeProvider переведён на 61 секунду вперёд; следующая неуспешная
///        попытка тем же ключом.
/// then:  401 (не 429) — метки строго новее границы now−60 с отсутствуют.
///        FR-080 AC «Окно скользится».
/// </summary>
public sealed class Ts164_SlidingWindowReleaseTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private const string ClientIp = "10.0.0.1";

    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS164_After61Seconds_LoginLimitIsReleased()
    {
        // given: 5 неуспешных входов teacher с неверным паролем (по 401) за минуту.
        using var client = B05SecurityClients.CreateClientWithIp(_factory, ClientIp);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failed = await B05SecurityClients.PostLoginAsync(
                client, SeedOptions.DefaultTeacherLogin, "ts164-wrong-password!");
            Assert.True(
                failed.StatusCode == HttpStatusCode.Unauthorized,
                $"Предусловие кейса: неуспешный вход {attempt} должен дать 401, " +
                $"фактически {(int)failed.StatusCode}: {await failed.Content.ReadAsStringAsync()}");
        }

        // when: TimeProvider переведён на 61 секунду вперёд; следующая неуспешная
        //       попытка тем же ключом (teacher, 10.0.0.1).
        _factory.Time.Advance(TimeSpan.FromSeconds(61));
        using var afterWindow = await B05SecurityClients.PostLoginAsync(
            client, SeedOptions.DefaultTeacherLogin, "ts164-wrong-password!");

        // then: 401 (не 429) — метки старше границы окна вычищены, лимит свободен.
        Assert.Equal(HttpStatusCode.Unauthorized, afterWindow.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, afterWindow.StatusCode);
    }
}
