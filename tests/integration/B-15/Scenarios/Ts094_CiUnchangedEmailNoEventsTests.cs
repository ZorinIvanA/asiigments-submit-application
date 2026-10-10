using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Observability;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-094 «Ci-неизменный email события и уведомления не создаёт» (negative, FR-021, P1).
///
/// given: Development (фикстура по умолчанию); пользователь role=student с email
///        a@b.ru создан прямым DI-сидом в IUserRepository тестового хоста; сессия —
///        cookie access_token с JWT HS256, минтым харнесом ключом Auth__JwtKey
///        тестового хоста; POST /auth/register и POST /auth/login в предусловиях
///        НЕ вызываются (CR-001, арбитраж a-017); лог — in-memory sink.
/// when:  PUT /me/profile {fullName:'Имя', email:'A@B.RU'}; инспекция журнала.
/// then:  200; записей event='email_changed' нет; уведомления Email.Dev нет.
///        FR-021 AC «Ci-неизменный email события не создаёт».
/// </summary>
public sealed class Ts094_CiUnchangedEmailNoEventsTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "ts094";
    private const string Email = "a@b.ru";
    private const string SameEmailCiUpper = "A@B.RU";

    private readonly B15WebAppFactory _factory;

    public Ts094_CiUnchangedEmailNoEventsTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EmailUnchangedCi_WritesNoSecurityEventAndNoNotification()
    {
        // given: Development; пользователь a@b.ru (DI-сид); сессия — минтованный access-cookie.
        var seeded = B15Harness.SeedStudent(_factory, fullName: "Текущее Имя", login: Login, email: Email);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

        // given: лог — in-memory sink; чистим стартовые записи хоста (изоляция сценария).
        _factory.LogSink.Clear();

        // when: отправка того же email в другом регистре (ci-неизменный).
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = SameEmailCiUpper,
        });

        // then: 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: записей event='email_changed' в категории Security нет.
        var changedEvents = _factory.LogSink.OfCategory(SecurityEventLogger.LogCategory)
            .Where(record => Equals(record.StateValue("event"), SecurityEventLogger.EmailChangedEvent))
            .ToList();
        Assert.Empty(changedEvents);

        // then: уведомления Email.Dev нет (sink очищен; любая запись категории была бы
        // уведомлением/письмом этого запроса — в Development после ci-неизменной смены их нет).
        Assert.Empty(_factory.LogSink.OfCategory(DevEmailSender.LogCategory));
    }
}
