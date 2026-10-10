using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-068 «recovery/request: существующий email — полный контракт»
/// (happy_path, FR-012, P0).
///
/// given: Development-стенд (B14RecoveryWebAppFactory); student01@example.com
///        зарегистрирован DI-сидом (реальный IPasswordHasher хоста); кодов нет
///        (хост фикстуры пуст, предусловие инспектируется); счётчик KDF обнулён —
///        делтой снимков IKdfCounter (IF-002, Δkdf не зависит от старта/сида);
///        тестовый log-sink с категориями (B14RecoveryLogSink);
/// when:  POST /api/v1/auth/recovery/request {email:'student01@example.com'};
/// then:  200; тело 0 байт и Content-Length: 0 (НЕ JSON-объект — ISS-014,
///        ADR-012); создан ровно один живой код с expiresAt=now+10 мин (TTL по
///        инжектируемым часам), attempts=0; IEmailSender вызван один раз (ровно
///        одна [DEV-EMAIL]-запись с адресатом, IF-005); запись с 6-значным кодом
///        присутствует ТОЛЬКО в категории 'EmailDev' (маркер [DEV-EMAIL], адресат,
///        код; NFR-006/ISS-003); Δkdf=0 (FR-012 AC «Существующий email»; ASM-005).
/// </summary>
public sealed class Ts068_RecoveryRequestExistingEmailTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    /// <summary>TTL кода восстановления — 10 минут (FR-012 п.3, IF-008).</summary>
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(10);

    /// <summary>Категория dev-писем — дословно спецификации (ISS-003/SEC-002, IF-005).</summary>
    private const string EmailDevCategory = "EmailDev";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts068_RecoveryRequestExistingEmailTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_ForExistingEmail_ReturnsEmptyBody_CreatesSingleLiveCode_EmailsOnce_ZeroKdf()
    {
        // given: пользователь зарегистрирован; кодов нет; точка отсчёта KDF снята.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        Assert.True(
            B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id) is null,
            "Предусловие кейса: у пользователя не должно быть кодов до запроса.");
        Assert.Equal(0, B14RecoveryRequestProbe.CountAllRecords(_factory.Services));
        var kdfBefore = B14KdfProbe.Snapshot(_factory.Services);
        using var client = B14Harness.Create(_factory);

        // when: запрос кода восстановления.
        var clockBefore = _factory.Clock.GetUtcNow();
        using var response = await B14RecoveryHarness.RequestRecoveryCodeAsync(client, Email);
        var clockAfter = _factory.Clock.GetUtcNow();

        // then: 200; тело 0 байт и Content-Length: 0 — НЕ JSON-объект (ISS-014).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: " +
            $"{await response.Content.ReadAsStringAsync()}");
        await B14RecoveryRequestProbe.AssertEmptyOkBodyAsync(response, "TS-068");

        // then: создан РОВНО ОДИН код, он живой: usedAt=null, attempts=0,
        // expiresAt=now+10 мин по инжектируемым часам (FR-012 п.3).
        Assert.Equal(
            1, B14RecoveryRequestProbe.CountAllRecords(_factory.Services));
        var live = B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id);
        Assert.True(
            live is not null,
            "Для существующего email обязан быть создан живой код восстановления.");
        Assert.Equal(0, live!.Attempts);
        Assert.Null(live.UsedAt);
        var expiresAt = B14RecoveryRequestProbe.ToUtcOffset(live.ExpiresAt);
        Assert.True(
            expiresAt >= clockBefore + CodeTtl && expiresAt <= clockAfter + CodeTtl,
            $"expiresAt кода обязан быть now+10 мин по инжектируемым часам " +
            $"(допустимый коридор [{clockBefore + CodeTtl:O}; {clockAfter + CodeTtl:O}]), " +
            $"фактически {expiresAt:O}.");

        // then: IEmailSender вызван один раз — ровно одна [DEV-EMAIL]-запись
        // с адресатом; код извлечён из неё (IF-005).
        var records = _factory.LogSink.Snapshot();
        var devEmailForRecipient = records
            .Where(record => record.Message.Contains("[DEV-EMAIL]", StringComparison.Ordinal))
            .Where(record => record.Message.Contains(Email, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            devEmailForRecipient.Count == 1,
            $"IEmailSender обязан быть вызван один раз: ожидалась одна [DEV-EMAIL]-запись " +
            $"для «{Email}», фактически {devEmailForRecipient.Count}.");
        var code = _factory.LogSink.GetLastRecoveryCodeForEmail(Email);

        // then: запись с кодом присутствует ТОЛЬКО в категории 'EmailDev'
        // (NFR-006/ISS-003/SEC-002).
        var recordsWithCode = records
            .Where(record => record.Message.Contains(code, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            recordsWithCode.Count > 0,
            "Запись с кодом восстановления обязана присутствовать в [DEV-EMAIL]-канале.");
        Assert.All(
            recordsWithCode,
            record => Assert.True(
                record.Category == EmailDevCategory,
                $"Код восстановления допустим только в категории «{EmailDevCategory}» (NFR-006), " +
                $"фактически запись категории «{record.Category}»: «{record.Message}»."));

        // then: Δkdf=0 (FR-012 AC «Существующий email»: коды — быстрый солёный
        // SHA-256, ASM-005).
        var kdfAfter = B14KdfProbe.Snapshot(_factory.Services);
        Assert.True(
            B14KdfProbe.TotalDelta(kdfBefore, kdfAfter) == 0,
            $"Ожидался Δkdf=0 на recovery/request, фактически " +
            $"{B14KdfProbe.TotalDelta(kdfBefore, kdfAfter)}.");
    }
}
