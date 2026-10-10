using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-069 «recovery/request: несуществующий email — неотличимое поведение»
/// (negative, FR-012, P0).
///
/// given: пользователя с lower(email)='nobody@example.com' не существует (хост
///        фикстуры свежий, этот email не сидируется); лимит 3/час не исчерпан;
///        тестовый log-sink; для эталона «тот же формат» в хосте есть
///        зарегистрированный пользователь (DI-сид) — контрольная отправка ему;
/// when:  POST /api/v1/auth/recovery/request {email:'nobody@example.com'};
/// then:  200; Content-Length: 0; в хранилище не появилось кодов (подсчёт ВСЕХ
///        записей кодов — B14RecoveryRequestProbe.CountAllRecords); запись
///        'EmailDev' выполнена с тем же форматом, что и для существующего email
///        (порционная нормализация: адресат и код заменяются плейсхолдерами,
///        шаблоны записей совпадают дословно; лог не раскрывает существование
///        учётной записи — FR-012 AC «Несуществующий email», IF-005).
/// </summary>
public sealed class Ts069_RecoveryRequestUnknownEmailTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string UnknownEmail = "nobody@example.com";
    private const string Login = "ts069";
    private const string ExistingEmail = "ts069@example.com";
    private const string FullName = "Студент Шестьдесят Девять";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts069_RecoveryRequestUnknownEmailTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_ForUnknownEmail_IsIndistinguishable_FromExistingEmail()
    {
        // given: несуществующий email; эталонный пользователь для светки формата.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: ExistingEmail,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        Assert.Equal(0, B14RecoveryRequestProbe.CountAllRecords(_factory.Services));
        using var client = B14Harness.Create(_factory);

        // when: запрос кода для несуществующего email.
        using var unknown = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, UnknownEmail);

        // then: 200; Content-Length: 0 (неотличимо от существующего email).
        Assert.True(
            unknown.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)unknown.StatusCode}: " +
            $"{await unknown.Content.ReadAsStringAsync()}");
        await B14RecoveryRequestProbe.AssertEmptyOkBodyAsync(unknown, "TS-069");

        // then: в хранилище не появилось кодов (ни для какого владельца).
        Assert.Equal(
            0,
            B14RecoveryRequestProbe.CountAllRecords(_factory.Services));

        // then: запись 'EmailDev' с тем же форматом, что для существующего email:
        // отправка выполнена (код сгенерирован ВСЕГДА, FR-012 п.2/п.4)…
        var unknownCode = _factory.LogSink.GetLastRecoveryCodeForEmail(UnknownEmail);

        // …контрольная отправка существующему email — эталон формата
        // (отдельный ключ лимитера, квоту кейса не расходует).
        using var existing = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, ExistingEmail);
        Assert.True(
            existing.StatusCode == HttpStatusCode.OK,
            $"Контрольная отправка существующему email обязана вернуть 200, фактически " +
            $"{(int)existing.StatusCode}: {await existing.Content.ReadAsStringAsync()}");
        var existingCode = _factory.LogSink.GetLastRecoveryCodeForEmail(ExistingEmail);

        var unknownRecord = SingleDevEmailRecord(UnknownEmail);
        var existingRecord = SingleDevEmailRecord(ExistingEmail);
        var normalizedUnknown = Normalize(unknownRecord.Message, UnknownEmail, unknownCode);
        var normalizedExisting = Normalize(existingRecord.Message, ExistingEmail, existingCode);
        Assert.True(
            string.Equals(normalizedUnknown, normalizedExisting, StringComparison.Ordinal),
            "Формат [DEV-EMAIL]-записи для несуществующего email обязан совпадать с форматом " +
            "для существующего (лог не раскрывает существование учётной записи, IF-005): " +
            $"существующий → «{normalizedExisting}», несуществующий → «{normalizedUnknown}».");

        // Пользователь не мог получить код: живых кодов у него нет.
        Assert.True(
            B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id) is not null,
            "Контроль: у эталонного существующего пользователя код создан.");
    }

    private B14LogRecord SingleDevEmailRecord(string email)
    {
        var matches = _factory.LogSink.Snapshot()
            .Where(record => record.Message.Contains("[DEV-EMAIL]", StringComparison.Ordinal))
            .Where(record => record.Message.Contains(email, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            matches.Count == 1,
            $"Ожидалась ровно одна [DEV-EMAIL]-запись для «{email}», фактически {matches.Count}.");
        return matches[0];
    }

    private static string Normalize(string message, string email, string code) =>
        message
            .Replace(email, "<EMAIL>", StringComparison.Ordinal)
            .Replace(code, "<CODE>", StringComparison.Ordinal);
}
