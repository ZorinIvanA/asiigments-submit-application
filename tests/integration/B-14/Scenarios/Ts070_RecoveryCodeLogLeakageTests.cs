using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-070 «Код восстановления не попадает в основные логи (ISS-003/SEC-002)»
/// (nfr, FR-012/NFR-006, P0).
///
/// given: тестовый log-sink разделяет категории; стенд (а) Development
///        (B14RecoveryWebAppFactory) и стенд (б) Production
///        (B14ProductionRecoveryWebAppFactory: заданы Auth__JwtKey и
///        НЕстандартный Seed__TeacherPassword — требования валидаторов Production);
///        код извлечён из [DEV-EMAIL]-записи стенда (а);
/// when:  POST recovery/request в обеих конфигурациях; проверка ВСЕХ записей sink;
/// then:  Development: ни одна запись вне категории 'EmailDev' не содержит код
///        (запись с кодом существует — она в 'EmailDev'); Production: ни одна
///        запись вообще не содержит код, ни одна запись не содержит адресата,
///        no-op warning доставлен (категория 'Hosting.Configuration', IF-005)
///        (FR-012 AC «Код не попадает в основной лог»; NFR-006).
/// </summary>
public sealed class Ts070_RecoveryCodeLogLeakageTests
    : IClassFixture<B14RecoveryWebAppFactory>,
      IClassFixture<B14ProductionRecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    /// <summary>Категория dev-писем — дословно спецификации (ISS-003/SEC-002, IF-005).</summary>
    private const string EmailDevCategory = "EmailDev";

    /// <summary>Категория no-op warning вне Development (IF-005, реестр категорий v2.2).</summary>
    private const string HostingConfigurationCategory = "Hosting.Configuration";

    private readonly B14RecoveryWebAppFactory _dev;
    private readonly B14ProductionRecoveryWebAppFactory _prod;

    public Ts070_RecoveryCodeLogLeakageTests(
        B14RecoveryWebAppFactory dev,
        B14ProductionRecoveryWebAppFactory prod)
    {
        _dev = dev;
        _prod = prod;
    }

    [Fact]
    public async Task RecoveryCode_IsLoggedOnlyInEmailDev_AndNeverInProduction()
    {
        // Стенд (а) Development: код извлечён из [DEV-EMAIL]-записи.
        var devSeeded = B14Harness.SeedUser(
            _dev,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var devClient = B14Harness.Create(_dev);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_dev, devClient, Email);

        // then Development: ни одна запись вне категории 'EmailDev' не содержит код.
        var devRecords = _dev.LogSink.Snapshot();
        var devRecordsWithCode = devRecords
            .Where(record => record.Message.Contains(code, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            devRecordsWithCode.Count > 0,
            "Предусловие кейса: [DEV-EMAIL]-запись с кодом обязана существовать в Development.");
        Assert.All(
            devRecordsWithCode,
            record => Assert.True(
                record.Category == EmailDevCategory,
                $"В Development код допустим только в категории «{EmailDevCategory}» (NFR-006/ISS-003), " +
                $"фактически запись категории «{record.Category}»: «{record.Message}»."));

        // Стенд (б) Production: Auth__JwtKey и нестандартный Seed__TeacherPassword
        // заданы фабрикой; тот же сценарий recovery/request.
        var prodSeeded = B14Harness.SeedUser(
            _prod,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var prodClient = B14Harness.Create(_prod);
        using var prodResponse = await B14RecoveryHarness.RequestRecoveryCodeAsync(prodClient, Email);

        Assert.True(
            prodResponse.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 в Production, фактически {(int)prodResponse.StatusCode}: " +
            $"{await prodResponse.Content.ReadAsStringAsync()}");
        await B14RecoveryRequestProbe.AssertEmptyOkBodyAsync(prodResponse, "TS-070");

        // Код в Production создан и сохранён — потому отсутствие его в журнале
        // осмысленно (не «нечего было логировать»).
        Assert.True(
            B14RecoveryCodeInspection.FindLiveRecoveryCode(_prod.Services, prodSeeded.Id) is not null,
            "Предусловие кейса: код восстановления в Production создан и сохранён.");

        // then Production: ни одна запись вообще не содержит код…
        var prodRecords = _prod.LogSink.Snapshot();
        Assert.DoesNotContain(
            prodRecords,
            record => record.Message.Contains(code, StringComparison.Ordinal));

        // …ни одна запись не содержит адресата (no-op warning без адресата)…
        Assert.DoesNotContain(
            prodRecords,
            record => record.Message.Contains(Email, StringComparison.Ordinal));

        // …и no-op warning доставлен ровно об этом (категория 'Hosting.Configuration',
        // Warning; сам его текст уже покрыт проверками выше — без кода и адресата).
        Assert.Contains(
            prodRecords,
            record => record.Category == HostingConfigurationCategory
                && record.Level == LogLevel.Warning);
    }
}
