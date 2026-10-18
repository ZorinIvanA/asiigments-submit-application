using System.Text;
using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B17.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-068 «recovery/request: существующий email — полный контракт»
/// (happy_path, FR-012, P0).
///
/// given: Development-стенд; student01@example.com зарегистрирован, кодов нет;
///        счётчик KDF обнулён (снимок пробы до запроса); тестовый log-sink
///        с категориями (B17LogSink).
/// when:  POST /api/v1/auth/recovery/request {email:'student01@example.com'}.
/// then:  200; тело 0 байт и Content-Length: 0 (НЕ JSON-объект — ISS-014);
///        создан ровно один живой код с expiresAt=now+10 мин, attempts=0;
///        IEmailSender вызван один раз; запись с 6-значным кодом присутствует
///        ТОЛЬКО в категории 'EmailDev' (маркер [DEV-EMAIL], адресат, код);
///        Δkdf=0 (FR-012 AC «Существующий email»).
/// </summary>
public sealed class Ts068_RecoveryRequestExistingEmailFullContractTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts068-student";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент ШестьдесятВосемь";
    private const string RequestBody = """{"email":"student01@example.com"}""";

    private readonly B17WebAppFactory _factory;

    public Ts068_RecoveryRequestExistingEmailFullContractTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_ExistingEmail_FullContract()
    {
        // Проба KDF подключается ДО старта хоста: MeterListener ловит только
        // инструменты, опубликованные после Start (см. B17KdfProbe).
        using var kdfProbe = new B17KdfProbe();

        // given: Development; student01@example.com зарегистрирован, кодов нет;
        //        счётчик KDF обнулён (снимок before после сида).
        using var client = B17Host.CreateClient(_factory);
        var user = B17Host.SeedStudent(_factory, Login, Email, FullName);
        var codes = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        Assert.Null(codes.FindLiveForUser(user.Id));
        Assert.Equal(0, B17Host.CountRecoveryCodeRecords(_factory));
        _factory.LogSink.Clear();
        var kdfBefore = kdfProbe.Total;

        // when: POST /auth/recovery/request {email:'student01@example.com'}.
        using var response = await B17Host.PostJsonAsync(client, B17Host.RecoveryRequestEndpoint, RequestBody);

        // then: 200; тело 0 байт и Content-Length: 0 (НЕ JSON-объект — ISS-014).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await B17Host.AssertEmptyBodyWithZeroContentLengthAsync(response, "recovery/request");

        // then: IEmailSender вызван один раз; код — строка из 6 ASCII-цифр
        //       (ведущие нули допустимы), адресат — запрошенный email.
        var sent = _factory.EmailSpy.SentRecoveryEmails;
        Assert.True(
            sent.Count == 1,
            $"Ожидался ровно один вызов IEmailSender, фактически {sent.Count}.");
        Assert.True(
            sent[0].Email.Equals(Email, StringComparison.OrdinalIgnoreCase),
            $"Ожидался адресат письма «{Email}», фактически «{sent[0].Email}».");
        var code = sent[0].Code;
        Assert.True(
            Regex.IsMatch(code, "^[0-9]{6}$", RegexOptions.CultureInvariant),
            $"Ожидался 6-значный код (ASCII-цифры), фактически «{code}».");

        // then: создан ровно один живой код: expiresAt=now+10 мин, attempts=0.
        Assert.Equal(1, B17Host.CountRecoveryCodeRecords(_factory));
        var live = codes.FindLiveForUser(user.Id);
        Assert.NotNull(live);
        Assert.Null(live.UsedAt);
        Assert.Equal(0, live.Attempts);
        var now = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        Assert.True(
            live.ExpiresAt > now.AddMinutes(9) && live.ExpiresAt <= now.AddMinutes(10),
            $"Ожидался TTL 10 минут (expiresAt=now+10мин), фактически expiresAt={live.ExpiresAt:O} при now={now:O}.");

        // then: запись с 6-значным кодом присутствует ТОЛЬКО в категории
        //       'EmailDev' — маркер [DEV-EMAIL], адресат, код.
        var devEmailRecords = _factory.LogSink.OfCategory(B17Host.DevEmailCategory);
        var withCode = devEmailRecords.Where(record => B17Host.ContainsCode(record, code)).ToList();
        Assert.True(
            withCode.Count == 1,
            $"В категории '{B17Host.DevEmailCategory}' ожидалась ровно одна запись с кодом; "
            + $"фактически {withCode.Count} из {devEmailRecords.Count} записей категории: "
            + $"[{string.Join(" | ", devEmailRecords.Select(r => r.Serialize()))}].");
        var devRecord = withCode[0].Serialize();
        Assert.True(
            devRecord.Contains(B17Host.DevEmailMarker, StringComparison.Ordinal),
            $"Запись письма без маркера {B17Host.DevEmailMarker}: {devRecord}");
        Assert.True(
            devRecord.Contains(Email, StringComparison.OrdinalIgnoreCase),
            $"Запись письма без адресата {Email}: {devRecord}");
        var outsideDevCategory = _factory.LogSink.Snapshot()
            .Where(record => !string.Equals(record.Category, B17Host.DevEmailCategory, StringComparison.Ordinal))
            .Where(record => B17Host.ContainsCode(record, code))
            .ToList();
        Assert.True(
            outsideDevCategory.Count == 0,
            $"Код восстановления появился вне категории '{B17Host.DevEmailCategory}' (NFR-006/ISS-003): "
            + $"[{string.Join(" | ", outsideDevCategory.Select(r => r.Serialize()))}].");

        // then: Δkdf=0 (FR-012: «Операций KDF в этом эндпойнте — 0»).
        var kdfAfter = kdfProbe.Total;
        Assert.True(
            kdfAfter == kdfBefore,
            $"Ожидалось Δkdf=0 на recovery/request, фактически Δkdf={kdfAfter - kdfBefore} "
            + $"(before={kdfBefore}, after={kdfAfter}).");
    }
}
