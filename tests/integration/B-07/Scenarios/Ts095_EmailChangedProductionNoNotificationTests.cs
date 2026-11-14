using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Observability;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-095 «Смена email в Production: событие есть, уведомления нет» (nfr,
/// FR-021 + NFR-006, P1).
///
/// given: Production-хост (<see cref="B07ProductionWebAppFactory"/>: Auth__JwtKey
///        и Seed__TeacherPassword по FR-006/FR-030; демо-набор всегда false,
///        FR-007); сессия пользователя с email old@x.ru (регистрация публичным API).
/// when:  PUT /me/profile {fullName:текущее, email:'new@x.ru'}; инспекция журнала.
/// then:  200; запись Security/email_changed присутствует; в категории Email.Dev
///        записей с кодом или уведомлением о смене нет (в Production категория
///        Email.Dev не пишется вовсе, IF-005: NotifyEmailChangedAsync — no-op).
///        FR-021 AC «Событие в Production без уведомления».
/// </summary>
public sealed class Ts095_EmailChangedProductionNoNotificationTests : IClassFixture<B07ProductionWebAppFactory>
{
    private const string Login = "ts095";
    private const string OldEmail = "old@x.ru";
    private const string NewEmail = "new@x.ru";
    private const string FullName = "Текущее Имя";

    private readonly B07ProductionWebAppFactory _factory;

    public Ts095_EmailChangedProductionNoNotificationTests(B07ProductionWebAppFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task EmailChange_InProduction_WritesSecurityEventWithoutDevNotification()
    {
        // given: Production-хост; сессия пользователя с email old@x.ru.
        var (client, userId) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: FullName, login: Login, email: OldEmail);
        _factory.LogSink.Clear();

        // when: фактическая смена email.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = FullName,
            email = NewEmail,
        });

        // then: 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: запись Security/email_changed присутствует с полями события.
        var changed = _factory.LogSink.OfCategory(SecurityEventLogger.LogCategory)
            .Where(record => Equals(record.StateValue("event"), SecurityEventLogger.EmailChangedEvent))
            .ToList();
        var record = Assert.Single(changed);
        Assert.True(
            Guid.TryParse(record.StateValue("userId")?.ToString(), out var recordedUserId)
            && recordedUserId == userId,
            $"Ожидался userId={userId}, фактически «{record.StateValue("userId")}».");
        Assert.Equal(OldEmail, record.StateValue("oldEmail")?.ToString());
        Assert.Equal(NewEmail, record.StateValue("newEmail")?.ToString());

        // then: в Production уведомления о смене в Email.Dev нет — после Clear()
        // категория Email.Dev пуста (код в Production-журнале запрещён, NFR-006).
        var devEmail = _factory.LogSink.OfCategory(DevEmailSender.LogCategory);
        Assert.True(
            devEmail.Count == 0,
            $"В Production категория Email.Dev должна отсутствовать; фактически {devEmail.Count} записей: [{string.Join("; ", devEmail.Select(r => r.Serialize()))}].");
    }
}
