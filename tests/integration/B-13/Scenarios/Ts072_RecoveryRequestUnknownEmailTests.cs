using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-072 (P0, negative; FR-012) «recovery/request: несуществующий email —
/// неотличимый 200».
/// given: пользователя с lower(email)='nobody@example.com' нет; лимит 3/час
///        не исчерпан (свежий хост фикстуры); Development; тестовый log-sink.
/// when:  POST {email:'nobody@example.com'}; затем — контрольный запрос на
///        существующий email (эталон формата записи 'EmailDev').
/// then:  200; пустое тело; в хранилище не появилось записей RecoveryCode;
///        запись 'EmailDev' выполнена в том же формате, что и для существующего
///        email (шаблон to/subject/body совпадает). FR-012 AC «Несуществующий
///        email».
/// </summary>
public sealed class Ts072_RecoveryRequestUnknownEmailTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string NobodyEmail = "nobody@example.com";
    private const string ExistingEmail = "existing72@example.com";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS072_Request_ForUnknownEmail_ReturnsIndistinguishableOkWithoutRecoveryRecords()
    {
        // given: хранилище кодов пусто (свежий хост); пользователя nobody нет.
        Assert.Empty(B13RecoverySeed.AllRecoveryCodes(_factory));
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST {email:'nobody@example.com'}.
        using var unknownResponse = await B13RecoveryApi.RecoveryRequestAsync(client, NobodyEmail);

        // then: 200 с пустым телом (неотличимо от существующего email — ISS-014).
        await B13RecoveryApi.AssertEmptyBodyOkAsync(unknownResponse);

        // then: в хранилище не появилось записей RecoveryCode.
        Assert.Empty(B13RecoverySeed.AllRecoveryCodes(_factory));

        // given (эталон формата): существующий email получает код тем же эндпойнтом.
        B13RecoverySeed.AddStudent(_factory, "existing72", ExistingEmail);
        using var existingResponse = await B13RecoveryApi.RecoveryRequestAsync(client, ExistingEmail);
        await B13RecoveryApi.AssertEmptyBodyOkAsync(existingResponse);

        // then: запись 'EmailDev' для несуществующего email — в ТОМ ЖЕ формате,
        // что и для существующего (шаблон [DEV-EMAIL] to=/subject=/body=, IF-005):
        // тема совпадает дословно, тело совпадает после замены кода-заполнителя.
        var devEmailEntries = B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink);
        Assert.Equal(2, devEmailEntries.Count);
        var unknownFormat = B13RecoveryLogAsserts.AssertDevEmailFormat(devEmailEntries[0]);
        var existingFormat = B13RecoveryLogAsserts.AssertDevEmailFormat(devEmailEntries[1]);
        Assert.Equal(NobodyEmail, unknownFormat.Groups["to"].Value);
        Assert.Equal(ExistingEmail, existingFormat.Groups["to"].Value);
        Assert.Equal(existingFormat.Groups["subject"].Value, unknownFormat.Groups["subject"].Value);
        var codePlaceholder = new System.Text.RegularExpressions.Regex(
            "(?<![0-9])[0-9]{6}(?![0-9])",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.Equal(
            codePlaceholder.Replace(existingFormat.Groups["body"].Value, "<code>"),
            codePlaceholder.Replace(unknownFormat.Groups["body"].Value, "<code>"));
    }
}
