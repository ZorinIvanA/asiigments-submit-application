using LabsApp.Auth;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

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
public sealed class Ts065_RecoveryRequestUnknownEmailTests(B12RecoveryDevSpyHost factory)
    : IClassFixture<B12RecoveryDevSpyHost>
{
    private const string UnknownEmail = "nobody@example.com";
    private const string ExistingEmail = "student65@example.com";

    private readonly B12RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS065_Request_ForUnknownEmail_ReturnsIndistinguishableEmptyBodyNoCodeSameEmailDevFormat()
    {
        // given: пользователя с nobody@example.com НЕ существует; существующий email —
        // DI-сид (эталон формата записи 'EmailDev', IF-005: один шаблон на обе ветки).
        var existingUser = B12RecoveryStore.AddStudent(_factory, "student65", ExistingEmail);
        var tokens = _factory.Services.GetRequiredService<ITokenService>();
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B12RecoveryHttp.CreateClient(_factory);

        // when: POST {email:'nobody@example.com'}.
        using var response = await B12RecoveryHarness.RequestRecoveryCodeAsync(client, UnknownEmail);

        // then: 200 с пустым телом (Content-Length: 0) — неотличимо от существующего.
        await B12RecoveryHttp.AssertEmptyBodyOkAsync(response);

        // then: в хранилище не появилось кодов RecoveryCode (сгенерированный «в никуда»
        // код не сохраняется).
        Assert.Empty(B12RecoveryStore.AllRecoveryCodes(_factory));

        // then: запись 'EmailDev' выполнена так же, как для существующего email:
        // тот же шаблон (маркер [DEV-EMAIL], to/subject/body), адресат присутствует.
        var allEntries = B12RecoveryLogs.DevEmailEntries(_factory.LogSink);
        var unknownEntry = Assert.Single(allEntries);
        B12RecoveryLogs.AssertDevEmailFormat(unknownEntry);
        Assert.Contains(UnknownEmail, unknownEntry.Message, StringComparison.Ordinal);

        // when/then (эталон формата): POST для существующего email — запись 'EmailDev'
        // в ТОМ ЖЕ шаблоне; оба запроса — в лимите 3/3600с (свои ключи email, FR-012).
        using var existingResponse = await B12RecoveryHarness.RequestRecoveryCodeAsync(client, ExistingEmail);
        await B12RecoveryHttp.AssertEmptyBodyOkAsync(existingResponse);

        allEntries = B12RecoveryLogs.DevEmailEntries(_factory.LogSink);
        Assert.Equal(2, allEntries.Count);
        var existingEntry = allEntries[1];
        var existingFormat = B12RecoveryLogs.AssertDevEmailFormat(existingEntry);
        Assert.Equal(ExistingEmail, existingFormat.Groups["to"].Value);

        // эталон — код в записи существующего email верифицируется по хэшу созданного
        // кода: формат записи принципиально одинаков, существование учётной записи
        // записью 'EmailDev' не раскрывается.
        var live = securityTokens.FindLiveForUser(existingUser.Id);
        Assert.NotNull(live);
        var code = B12RecoveryLogs.VerifiedCode(existingEntry, tokens, live.CodeHash);
        Assert.Matches("^[0-9]{6}$", code);
    }
}
