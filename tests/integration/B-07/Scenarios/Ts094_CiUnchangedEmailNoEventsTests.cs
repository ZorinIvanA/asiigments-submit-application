using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Observability;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-094 «Ci-неизменный email события и уведомления не создаёт» (negative, FR-021, P1).
///
/// given: Development; сессия с email a@b.ru (собственная учётка регистрации).
/// when:  PUT /me/profile {fullName:'Имя', email:'A@B.RU'}; инспекция журнала.
/// then:  200; записей event='email_changed' нет; уведомления Email.Dev нет.
///        FR-021 AC «Ci-неизменный email события не создаёт».
/// </summary>
public sealed class Ts094_CiUnchangedEmailNoEventsTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "ts094";
    private const string Email = "a@b.ru";
    private const string FullName = "Имя";

    private readonly B07WebAppFactory _factory;

    public Ts094_CiUnchangedEmailNoEventsTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EmailChange_CaseInsensitiveSameEmail_CreatesNoEventAndNoNotification()
    {
        // given: Development; сессия с email a@b.ru.
        var (client, _) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: FullName, login: Login, email: Email);
        _factory.LogSink.Clear();

        // when: email, равный старому без учёта регистра.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = FullName,
            email = "A@B.RU",
        });

        // then: 200 — свой (ci-неизменённый) email конфликтом не считается.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: записей event='email_changed' нет.
        var changed = _factory.LogSink.OfCategory(SecurityEventLogger.LogCategory)
            .Where(record => Equals(record.StateValue("event"), SecurityEventLogger.EmailChangedEvent))
            .ToList();
        Assert.True(
            changed.Count == 0,
            $"Ожидалось 0 записей Security/email_changed, фактически {changed.Count}: [{string.Join("; ", changed.Select(r => r.Serialize()))}].");

        // then: уведомления Email.Dev нет — после Clear() категория Email.Dev пуста.
        var devEmail = _factory.LogSink.OfCategory(DevEmailSender.LogCategory);
        Assert.True(
            devEmail.Count == 0,
            $"Ожидалось 0 записей Email.Dev, фактически {devEmail.Count}: [{string.Join("; ", devEmail.Select(r => r.Serialize()))}].");
    }
}
