using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-065 (P0, negative; FR-012) «recovery/request: несуществующий email —
/// неотличимый 200».
/// given: пользователя с lower(email)='nobody@example.com' не существует; лимит
///        3/час не исчерпан; Development; log-sink подключён.
/// when:  POST {email:'nobody@example.com'}.
/// then:  200 с пустым телом (Content-Length: 0); в хранилище не появилось кодов
///        RecoveryCode; запись 'EmailDev' выполнена в том же формате, что и для
///        существующего email (существование учётной записи не раскрывается).
///        FR-012 AC «Несуществующий email».
/// </summary>
public sealed class Ts065_RecoveryRequestUnknownEmailTests(B11RecoveryDevSpyHost factory)
    : IClassFixture<B11RecoveryDevSpyHost>
{
    private const string UnknownEmail = "nobody@example.com";
    private const string ExistingEmail = "student65@example.com";

    private readonly B11RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS065_Request_ForUnknownEmail_ReturnsIndistinguishableEmptyBodyNoCodeSameEmailDevFormat()
    {
        // given: пользователя с nobody@example.com НЕ существует; существующий email —
        // DI-сид (эталон формата записи 'EmailDev', IF-005: один шаблон на обе ветки).
        var existingUser = B11RecoverySeed.AddStudent(_factory, "student65", ExistingEmail);
        var tokens = _factory.Services.GetRequiredService<ITokenService>();
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = HostClients.Create(_factory);

        // when: POST {email:'nobody@example.com'}.
        using var response = await B11RecoveryApi.RecoveryRequestAsync(client, UnknownEmail);

        // then: 200 с пустым телом (Content-Length: 0) — неотличимо от существующего.
        await B11RecoveryApi.AssertEmptyBodyOkAsync(response);

        // then: в хранилище не появилось кодов RecoveryCode (сгенерированный «в никуда»
        // код не сохраняется).
        Assert.Empty(B11RecoverySeed.AllRecoveryCodes(_factory));

        // then: запись 'EmailDev' выполнена так же, как для существующего email:
        // тот же шаблон (маркер [DEV-EMAIL], to/subject/body), адресат присутствует.
        var allEntries = B11RecoveryLogs.DevEmailEntries(_factory.LogSink);
        var unknownEntry = Assert.Single(allEntries);
        B11RecoveryLogs.AssertDevEmailFormat(unknownEntry);
        Assert.Contains(UnknownEmail, unknownEntry.Message, StringComparison.Ordinal);

        // when/then (эталон формата): POST для существующего email — запись 'EmailDev'
        // в ТОМ ЖЕ шаблоне; оба запроса — в лимите 3/3600с (свои ключи email, FR-012).
        using var existingResponse = await B11RecoveryApi.RecoveryRequestAsync(client, ExistingEmail);
        await B11RecoveryApi.AssertEmptyBodyOkAsync(existingResponse);

        allEntries = B11RecoveryLogs.DevEmailEntries(_factory.LogSink);
        Assert.Equal(2, allEntries.Count);
        var existingEntry = allEntries[1];
        var existingFormat = B11RecoveryLogs.AssertDevEmailFormat(existingEntry);
        Assert.Equal(ExistingEmail, existingFormat.Groups["to"].Value);

        // эталон — код в записи существующего email верифицируется по хэшу созданного
        // кода: формат записи принципиально одинаков, существование учётной записи
        // записью 'EmailDev' не раскрывается.
        var live = securityTokens.FindLiveForUser(existingUser.Id);
        Assert.NotNull(live);
        var code = B11RecoveryLogs.VerifiedCode(existingEntry, tokens, live.CodeHash);
        Assert.Matches("^[0-9]{6}$", code);
    }
}
