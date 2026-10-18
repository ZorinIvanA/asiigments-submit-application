using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-095 «Смена email в Production: событие есть, уведомления нет» (nfr, FR-021 + NFR-006, P1).
///
/// given: Production-фикстура: Production-хост с валидными секретами, заданными
///        харнесом (Auth__JwtKey ≥32 симв. и ≠ dev-ключу; Seed__TeacherPassword по
///        правилам §8 — значения известны харнесу); пользователь role=student с
///        email old@x.ru создан прямым DI-сидом в IUserRepository тестового хоста;
///        uuid известен; сессия — cookie access_token с JWT HS256, минтым харнесом
///        ключом Auth__JwtKey этого Production-хоста; POST /auth/register и
///        POST /auth/login в предусловиях НЕ вызываются (CR-001, арбитраж a-017);
///        лог — in-memory sink.
/// when:  PUT /me/profile {fullName:текущее, email:'new@x.ru'}; инспекция журнала.
/// then:  200; запись Security/email_changed присутствует; в категории Email.Dev
///        записей с кодом или уведомлением о смене нет.
///        FR-021 AC «Событие в Production без уведомления».
/// </summary>
public sealed class Ts095_EmailChangedProductionNoNotificationTests : IClassFixture<B15ProductionWebAppFactory>
{
    private const string Login = "ts095";
    private const string OldEmail = "old@x.ru";
    private const string NewEmail = "new@x.ru";
    private const string FullName = "Текущее Имя";

    private readonly B15ProductionWebAppFactory _factory;

    public Ts095_EmailChangedProductionNoNotificationTests(B15ProductionWebAppFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task EmailChangeInProduction_WritesSecurityEventWithoutDevNotification()
    {
        // given: Production-хост; пользователь old@x.ru (DI-сид); сессия минтируется
        // ключом Auth__JwtKey ИМЕННО этого хоста (ITokenService из factory.Services).
        var seeded = B15Harness.SeedStudent(_factory, fullName: FullName, login: Login, email: OldEmail);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

        // given: лог — in-memory sink; чистим стартовые записи хоста (изоляция сценария).
        _factory.LogSink.Clear();

        // when: фактическая смена email.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = FullName,
            email = NewEmail,
        });

        // then: 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: запись Security/email_changed присутствует (с полями события).
        var changedEvents = _factory.LogSink.OfCategory(SecurityEventLogger.LogCategory)
            .Where(record => Equals(record.StateValue("event"), SecurityEventLogger.EmailChangedEvent))
            .ToList();
        var changed = Assert.Single(changedEvents);
        Assert.True(
            Guid.TryParse(changed.StateValue("userId")?.ToString(), out var recordedUserId)
            && recordedUserId == seeded.Id,
            $"Ожидался userId={seeded.Id}, фактически «{changed.StateValue("userId")}».");
        Assert.Equal(OldEmail, changed.StateValue("oldEmail")?.ToString());
        Assert.Equal(NewEmail, changed.StateValue("newEmail")?.ToString());

        // then: в Production категория Email.Dev не содержит ни кода, ни уведомления
        // о смене (sink очищен; в Production уведомление — no-op, IF-005, категория
        // Email.Dev существует только в Development, NFR-006).
        Assert.Empty(_factory.LogSink.OfCategory(DevEmailSender.LogCategory));

        // then: хранилище подтверждает смену (email обновлён).
        Assert.Equal(NewEmail, B15Harness.UserByEmail(_factory, NewEmail).Email);
    }
}
