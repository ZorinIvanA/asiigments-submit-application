using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-069 «Запрос кода для существующего email: 200, один живой код, письмо в
/// Email.Dev» (happy_path, FR-017, P0).
///
/// given: Development; пользователь с email a@b.ru (DI-сид); лог пишет в
///        in-memory sink (B08LogSink).
/// when:  POST /api/v1/auth/recovery/request {email:' a@B.RU '} (trim + ci
///        должны нормализовать адрес); инспекция RecoveryCode и лога.
/// then:  200 с пустым телом-объектом {}; в хранилище ровно один живой код
///        пользователя с expiresAt=now+10 мин; в категории Email.Dev есть запись
///        письма с адресом a@b.ru и строкой из ровно 6 ASCII-цифр (проверяется
///        шаблоном, значение не фиксируется). FR-017 AC «Существующий email».
///
/// Единственность кода (доработка CR-002): проверяется не только
/// GetLiveForUser (последний живой код по контракту IF-015), но и полное число
/// записей RecoveryCode в хранилище — ровно одна.
/// </summary>
public sealed class Ts069_RecoveryRequestExistingEmailTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts069-student";
    private const string Email = "a@b.ru";
    private const string FullName = "Студент ШестьдесятДевять";

    /// <summary>Тело запроса кейса: адрес с пробелами и прописными — нормализация обязательна.</summary>
    private const string RequestBody = """{"email":" a@B.RU "}""";

    private readonly B08WebAppFactory _factory;

    public Ts069_RecoveryRequestExistingEmailTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_ExistingEmail_Always200StoresSingleLiveCodeAndSendsDevEmail()
    {
        // given: Development; пользователь с email a@b.ru; живых кодов нет.
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName);
        var codes = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        Assert.Null(codes.FindLiveForUser(user.Id));
        var recordsBefore = B08Host.CountRecoveryCodeRecords(_factory);
        _factory.LogSink.Clear();

        // when: POST /auth/recovery/request {email:' a@B.RU '}.
        using var response = await client.PostAsync(B08Host.RecoveryRequestEndpoint, RequestBody);

        // then: 200 с пустым телом-объектом {}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        ResponseAssertions.AssertEmptyBody(body, "recovery/request");

        // then: в хранилище ровно один живой код пользователя с expiresAt=now+10 мин.
        var live = codes.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.True(live.UsedAt is null, "Живой код не должен быть погашен.");

        // then: запись кода в хранилище ЕДИНСТВЕННА (доработка CR-002: кейс
        //       требует «ровно один живой код», но GetLiveForUser по контракту
        //       IF-015 возвращает лишь последний живой код — реализация,
        //       создающая за один request два живых кода, иначе прошла бы;
        //       хост фикстуры свежий, поэтому полное число записей равно числу
        //       созданных этим запросом).
        var recordsAfter = B08Host.CountRecoveryCodeRecords(_factory);
        Assert.True(
            recordsAfter == 1,
            $"Ожидалась ровно одна запись RecoveryCode в хранилище, фактически {recordsAfter} "
            + $"(до запроса: {recordsBefore}) — «одновременно жив максимум один код», FR-017.");
        var now = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        Assert.True(
            live.ExpiresAt > now.AddMinutes(9) && live.ExpiresAt <= now.AddMinutes(10),
            $"Ожидался TTL 10 минут (expiresAt=now+10мин), фактически expiresAt={live.ExpiresAt:O} при now={now:O}.");

        // then: в категории Email.Dev есть запись письма с адресом a@b.ru
        //       и строкой из ровно 6 ASCII-цифр (шаблон, значение не фиксируется).
        var devEmailRecords = _factory.LogSink.OfCategory(DevEmailSender.LogCategory);
        var matching = devEmailRecords
            .Where(record => record.Serialize().Contains("a@b.ru", StringComparison.OrdinalIgnoreCase)
                && ResponseAssertions.HasSixDigitRun(record))
            .ToList();
        Assert.True(
            matching.Count >= 1,
            $"В категории Email.Dev нет записи письма с адресом a@b.ru и 6-значным кодом; "
            + $"фактические записи: [{string.Join(" | ", devEmailRecords.Select(r => r.Serialize()))}].");
    }
}
