using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-023 «Recovery: 429 не раскрывает существование email»
/// (negative, FR-004 + FR-012, P0).
///
/// given: 3 запроса POST /auth/recovery/request на 'unknown@example.com' за час
///        (лимит 3/3600 исчерпан); существует студент student01@example.com без
///        исчерпанного лимита.
/// when:  4-й запрос на 'unknown@example.com' и 4-й запрос на
///        'student01@example.com' после трёх его запросов.
/// then:  оба — 429 RATE_LIMITED с одинаковыми статусом и сообщением
///        'Слишком много попыток. Повторите позже' (оракула существования нет).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): исчерпание-429 для
/// существующего/несуществующего email прежней волны зоны покрыто
/// Ts020_RecoveryQuotaPerEmailIndependent (независимость ключей); файлы прежних
/// волн не изменялись.
/// </summary>
public sealed class Ts023_RecoveryLimitNoEmailOracleTests : IClassFixture<B09WebAppFactory>
{
    private const string UnknownEmail = "unknown@example.com";
    private const string ExistingEmail = "student01@example.com";
    private const string ExistingLogin = "ts023student01";
    private const string RegistrationIp = "10.0.0.73";

    private readonly B09WebAppFactory _factory;

    public Ts023_RecoveryLimitNoEmailOracleTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FourthRequest_ExhaustedUnknownOrExistingEmail_Same429SameMessage()
    {
        // given: существует студент student01@example.com (собственный IP
        // регистрации — регистровый лимит не касается recovery-ключей).
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Третий Студент", ExistingLogin, ExistingEmail, ip: RegistrationIp);

        // given: лимит 'unknown@example.com' исчерпан — 3 запроса за час
        // (каждый 200: оракула существования нет).
        var anonymous = B09HostClients.Create(_factory);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await B09HostClients.RequestRecoveryCodeAsync(_factory, UnknownEmail, anonymous);
        }

        // given: три запроса на существующий email — его ключ исчерпается 4-м.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await B09HostClients.RequestRecoveryCodeAsync(_factory, ExistingEmail, anonymous);
        }

        // when: 4-й запрос на 'unknown@example.com' и 4-й запрос на
        // 'student01@example.com' после трёх его запросов.
        using var fourthUnknown = await anonymous.PostAsJsonAsync(
            B09HostClients.RecoveryRequestEndpoint, new { email = UnknownEmail });
        using var fourthExisting = await B09HostClients.Create(_factory).PostAsJsonAsync(
            B09HostClients.RecoveryRequestEndpoint, new { email = ExistingEmail });

        // then: оба — 429 RATE_LIMITED с одинаковыми статусом и сообщением.
        var unknownBody = await B09Assertions.ParseObjectAsync(
            fourthUnknown, HttpStatusCode.TooManyRequests, $"4-й recovery/request на {UnknownEmail}");
        var existingBody = await B09Assertions.ParseObjectAsync(
            fourthExisting, HttpStatusCode.TooManyRequests, $"4-й recovery/request на {ExistingEmail}");
        B09Assertions.MessageIs(unknownBody, B09AuthSupport.RateLimitedMessage);
        B09Assertions.MessageIs(existingBody, B09AuthSupport.RateLimitedMessage);
        Assert.True(
            fourthUnknown.StatusCode == fourthExisting.StatusCode
                && string.Equals(
                    unknownBody.GetProperty("message").GetString(),
                    existingBody.GetProperty("message").GetString(),
                    StringComparison.Ordinal),
            "429 для несуществующего и существующего email обязаны совпадать статусом и сообщением " +
            "(оракула существования нет).");
    }
}
