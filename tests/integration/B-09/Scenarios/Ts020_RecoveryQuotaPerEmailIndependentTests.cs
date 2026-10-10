using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-020 «Recovery: квота email независима — исчерпание одного ключа не блокирует
/// другой email» (negative, FR-004, FR-012, P0).
///
/// given: ключ лимитера recovery_request для 'unknown@example.com' исчерпан
///        (3 запроса за последний час); существует пользователь с email
///        'student01@example.com'; запросов по ключу 'student01@example.com'
///        в текущем окне не было.
/// when:  4-й POST /api/v1/auth/recovery/request {email:'unknown@example.com'};
///        затем POST {email:'student01@example.com'}.
/// then:  первый — 429, message 'Слишком много попыток. Повторите позже';
///        второй — 200 с пустым телом (Content-Length: 0). Исчерпание квоты
///        одного ключа НЕ переносится на другой (матрица FR-004: ключ
///        recovery_request = lower(trim(email)); FR-012: ответ 200 с ПУСТЫМ
///        телом в любом случае). Исчерпание-429 для существующего и
///        несуществующего email отдельными прогонами покрыто TS-072.
/// </summary>
public sealed class Ts020_RecoveryQuotaPerEmailIndependentTests : IClassFixture<B09WebAppFactory>
{
    private const string ExhaustedEmail = "unknown@example.com";
    private const string OtherEmail = "student01@example.com";
    private const string OtherLogin = "ts020student01";
    private const string RegistrationIp = "10.0.0.21";

    private readonly B09WebAppFactory _factory;

    public Ts020_RecoveryQuotaPerEmailIndependentTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ExhaustedEmailQuota_DoesNotLeakToOtherEmailKey()
    {
        // given: пользователь с email 'student01@example.com' существует
        // (регистрация с собственного IP — регистровый лимит не касается
        // recovery-ключей).
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцатый Студент", OtherLogin, OtherEmail, ip: RegistrationIp);

        // given: ключ 'unknown@example.com' исчерпан — 3 запроса за час
        // (каждый — 200 с пустым телом: оракула существования нет).
        var anonymous = B09HostClients.Create(_factory);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await B09HostClients.RequestRecoveryCodeAsync(_factory, ExhaustedEmail, anonymous);
        }

        // when: 4-й запрос на исчерпанный email; затем запрос на другой email.
        using var fourth = await anonymous.PostAsJsonAsync(
            B09HostClients.RecoveryRequestEndpoint, new { email = ExhaustedEmail });
        using var other = await B09HostClients.Create(_factory).PostAsJsonAsync(
            B09HostClients.RecoveryRequestEndpoint, new { email = OtherEmail });

        // then: 4-й запрос — 429 с дословным message.
        var fourthBody = await B09Assertions.ParseObjectAsync(
            fourth,
            HttpStatusCode.TooManyRequests,
            $"4-й recovery/request на исчерпанный ключ {ExhaustedEmail}");
        B09Assertions.MessageIs(fourthBody, ContractTexts.RateLimited);

        // then: запрос по НЕисчерпанному ключу — 200 с пустым телом
        // (Content-Length: 0): квота одного ключа не переносится на другой.
        Assert.True(
            other.StatusCode == HttpStatusCode.OK,
            $"recovery/request по {OtherEmail} (ключ не исчерпан) должен вернуть 200, " +
            $"фактически {(int)other.StatusCode}: {await other.Content.ReadAsStringAsync()}");
        Assert.True(
            other.Content.Headers.ContentLength == 0,
            $"Ожидался ответ 200 с пустым телом (Content-Length: 0), фактически " +
            $"Content-Length: {other.Content.Headers.ContentLength?.ToString() ?? "отсутствует"}.");
        await B09Assertions.BodyIsEmptyAsync(other, $"recovery/request по {OtherEmail}");
    }
}
