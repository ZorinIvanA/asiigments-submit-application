using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-035 (P0, nfr; FR-010, NFR-005) «Хэш кода восстановления: PBKDF2 с
/// собственной солью, без открытого значения».
///
/// given: Development; пользователь с email a@b.ru (DI-сид); выполнен
///        POST /auth/recovery/request; код извлечён из категории лога Email.Dev.
/// when:  инспекция IRecoveryCodeRepository: codeHash, соль, верификация.
/// then:  codeHash — PBKDF2-HMAC-SHA256 (600000 итераций) с собственной случайной
///        солью ≥16 байт; открытого 6-значного значения в хранилище нет;
///        verify(код, hash)=true; verify(другой 6-значный код, hash)=false.
///        FR-010 AC «Хэш кода восстановления».
/// </summary>
public sealed class Ts035_RecoveryCodeHashTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private const string Login = "ts035-user";
    private const string Email = "a@b.ru";
    private const string FullName = "Студент ТриДцатьПять";

    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS035_RecoveryCodeHash_IsOwnSaltPbkdf2WithoutPlaintext()
    {
        // given: Development; пользователь с email a@b.ru; живых кодов нет.
        using var client = B05SecurityClients.CreateClient(_factory);
        var user = B05SecurityClients.SeedStudent(_factory, Login, Email, FullName);
        var codes = _factory.Services.GetRequiredService<IRecoveryCodeRepository>();
        Assert.Null(codes.GetLiveForUser(user.Id));
        _factory.LogSink.Clear();

        // given: выполнен POST /auth/recovery/request; код — из категории Email.Dev.
        using var response = await client.PostAsJsonAsync(
            B05SecurityClients.RecoveryRequestEndpoint, new { email = Email });
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: recovery/request для существующего email обязан вернуть 200, " +
            $"фактически {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var code = ExtractCodeFromDevEmailLog();
        Assert.False(string.IsNullOrEmpty(code), "Код восстановления не извлечён из Email.Dev.");

        // when: инспекция IRecoveryCodeRepository — codeHash живого кода.
        var live = codes.GetLiveForUser(user.Id);
        Assert.True(
            live is not null,
            "Предусловие кейса: после recovery/request в хранилище обязан быть живой код пользователя.");

        // then: codeHash — PBKDF2-HMAC-SHA256 (600000 итераций), собственная соль ≥16 байт.
        B05SecurityClients.AssertStoredKdfFormat(live!.CodeHash, "codeHash пользователя TS-035");

        // then: открытого 6-значного значения в хранилище нет.
        Assert.NotEqual(code, live.CodeHash);
        Assert.DoesNotContain(code, live.CodeHash, StringComparison.Ordinal);

        // then: verify(код, hash)=true; verify(другой 6-значный код, hash)=false.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        Assert.True(hasher.VerifyRecoveryCode(live.CodeHash, code!),
            "Верификация извлечённого кода обязана дать true.");
        Assert.False(hasher.VerifyRecoveryCode(live.CodeHash, OtherSixDigitCode(code!)),
            "Верификация другого 6-значного кода обязана дать false.");
    }

    /// <summary>Детерминированно другой 6-значный код (первая цифра инвертируется).</summary>
    private static string OtherSixDigitCode(string code) =>
        (code[0] == '0' ? '1' : '0') + code[1..];

    /// <summary>
    /// Извлекает код восстановления из записи категории Email.Dev (единственное
    /// место журнала с кодом): структурированное поле code, иначе 6-значная серия.
    /// </summary>
    private string? ExtractCodeFromDevEmailLog()
    {
        var devEmailRecords = _factory.LogSink.OfCategory(DevEmailSender.LogCategory);
        var record = devEmailRecords.FirstOrDefault(
            entry => entry.Serialize().Contains(Email, StringComparison.OrdinalIgnoreCase));
        Assert.True(
            record is not null,
            "В категории Email.Dev нет записи письма для a@b.ru; записи: " +
            $"[{string.Join(" | ", devEmailRecords.Select(entry => entry.Serialize()))}].");

        var structured = record!.StateValue("code") as string;
        if (structured is not null && Regex.IsMatch(structured, "^[0-9]{6}$"))
        {
            return structured;
        }

        var serialized = record.Serialize();
        var match = Regex.Match(serialized, "(?<![0-9])[0-9]{6}(?![0-9])");
        return match.Success ? match.Value : null;
    }
}
