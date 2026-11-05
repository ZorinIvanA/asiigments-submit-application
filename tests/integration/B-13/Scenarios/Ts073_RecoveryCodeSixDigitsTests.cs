using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-073 (P1, boundary; FR-012) «Recovery: код — ровно 6 ASCII-цифр, ведущие
/// нули допустимы».
/// given: Development; выполнено ≥10 запросов recovery/request на разные
///        существующие email (DI-сид 10 учёток; лимит recovery_request 3/час —
///        по lower(email), у каждой учётки не более одного запроса, FR-004).
/// when:  извлечение кодов из записей 'EmailDev' тестового log-sink.
/// then:  каждый код соответствует ^[0-9]{6}$ (FR-012: «6-значный код
///        (ASCII-цифры, криптографический ГСЧ, ведущие нули допустимы)»).
/// </summary>
public sealed class Ts073_RecoveryCodeSixDigitsTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const int RequestCount = 10;

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS073_RecoveryCodes_FromEmailDevEntries_AllMatchSixAsciiDigitsPattern()
    {
        // given: 10 существующих email (DI-сид учёток без пароля — вход не нужен).
        var emails = Enumerable
            .Range(1, RequestCount)
            .Select(index => $"ts073student{index:00}@example.com")
            .ToArray();
        foreach (var (email, index) in emails.Select((email, index) => (email, index)))
        {
            B13RecoverySeed.AddStudent(_factory, $"ts073student{index + 1:00}", email);
        }

        // given: ≥10 запросов recovery/request на разные существующие email.
        using var client = B13RecoveryHarness.CreateClient(_factory);
        foreach (var email in emails)
        {
            using var response = await B13RecoveryApi.RecoveryRequestAsync(client, email);
            await B13RecoveryApi.AssertEmptyBodyOkAsync(response);
        }

        // when: извлечение кодов из записей 'EmailDev' тестового log-sink
        // (по записи на каждый запрос; код — 6-цифровая группа текста записи).
        var devEmailEntries = B13RecoveryLogAsserts.DevEmailEntries(_factory.LogSink);
        Assert.Equal(RequestCount, devEmailEntries.Count);
        var codes = emails
            .Select(email => _factory.LogSink.GetLastRecoveryCodeForEmail(email))
            .ToArray();

        // then: каждый код соответствует ^[0-9]{6}$ (ведущие нули допустимы —
        // шаблон не отсекает их; ASCII-цифры — класс [0-9]).
        Assert.Equal(RequestCount, codes.Length);
        Assert.All(codes, code => Assert.Matches("^[0-9]{6}$", code));
    }
}
