using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Observability;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-093 «Событие Security/email_changed при фактической смене email (dev)»
/// (data_integrity, FR-021 + NFR-006, P0).
///
/// given: Development (фикстура по умолчанию); сессия пользователя с email
///        old@x.ru; лог — in-memory sink тестового хоста.
/// when:  PUT /me/profile {fullName:текущее, email:'new@x.ru'}; инспекция журнала.
/// then:  200; в журнале запись категории Security с event='email_changed',
///        userId пользователя, oldEmail='old@x.ru', newEmail='new@x.ru';
///        сериализованная запись не содержит маркеров NFR-006
///        (пароль/currentPassword/access_token/refresh_token/resetToken);
///        в Development категория Email.Dev содержит уведомление с адресом
///        old@x.ru без кодов. FR-021 AC «Событие безопасности при фактической
///        смене email» (SEC-005).
/// </summary>
public sealed class Ts093_EmailChangedSecurityEventDevTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "ts093";
    private const string OldEmail = "old@x.ru";
    private const string NewEmail = "new@x.ru";
    private const string FullName = "Текущее Имя";

    /// <summary>«Код» — 6 цифр подряд (NFR-006: код восстановления — строка из 6 цифр).</summary>
    private static readonly Regex SixDigitRun = new(@"[0-9]{6}", RegexOptions.Compiled);

    private readonly B07WebAppFactory _factory;

    public Ts093_EmailChangedSecurityEventDevTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EmailChange_WritesSecurityEventAndDevNotification()
    {
        // given: Development; сессия пользователя с email old@x.ru.
        var (client, userId) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: FullName, login: Login, email: OldEmail);

        // given: лог — in-memory sink; чистим стартовые записи хоста (изоляция сценария).
        _factory.LogSink.Clear();

        // when: фактическая смена email.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = FullName,
            email = NewEmail,
        });

        // then: 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // then: запись категории Security с event='email_changed' и полями события.
        var securityRecords = _factory.LogSink.OfCategory(SecurityEventLogger.LogCategory)
            .Where(record => Equals(record.StateValue("event"), SecurityEventLogger.EmailChangedEvent))
            .ToList();
        var changed = Assert.Single(securityRecords);
        Assert.True(
            Guid.TryParse(changed.StateValue("userId")?.ToString(), out var recordedUserId)
            && recordedUserId == userId,
            $"Ожидался userId={userId}, фактически «{changed.StateValue("userId")}».");
        Assert.Equal(OldEmail, changed.StateValue("oldEmail")?.ToString());
        Assert.Equal(NewEmail, changed.StateValue("newEmail")?.ToString());

        // then: сериализованная запись не содержит маркеров NFR-006.
        LogMarkerAssertions.HasNoSecretMarkers(changed);

        // then: в Development категория Email.Dev содержит уведомление о смене
        // на СТАРЫЙ адрес — без кодов (6-цифровых последовательностей нет).
        var notifications = _factory.LogSink.OfCategory(DevEmailSender.LogCategory)
            .Where(record => string.Equals(
                record.StateValue("subject")?.ToString(),
                DevEmailSender.EmailChangedSubject,
                StringComparison.Ordinal))
            .ToList();
        var notification = Assert.Single(notifications);
        Assert.Equal(OldEmail, notification.StateValue("to")?.ToString());
        Assert.False(
            SixDigitRun.IsMatch(notification.Serialize()),
            $"Уведомление Email.Dev содержит 6 цифр подряд (код): {notification.Serialize()}");
    }
}
