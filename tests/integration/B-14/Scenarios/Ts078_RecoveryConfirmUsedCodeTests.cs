using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-078 «recovery/confirm: использованный код — 400» (negative, FR-013, P1).
///
/// given: код уже погашен успешным подтверждением (200 с resetToken — код
///        получен из [DEV-EMAIL]-записи sink);
/// when:  повторный POST /api/v1/auth/recovery/confirm {email, code} с тем же
///        кодом;
/// then:  400 'Код восстановления не подходит' (одноразовость кода; FR-013).
/// </summary>
public sealed class Ts078_RecoveryConfirmUsedCodeTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "ts078";
    private const string Email = "ts078@example.com";
    private const string FullName = "Студент Семьдесят Восемь";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts078_RecoveryConfirmUsedCodeTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RepeatConfirm_WithUsedCode_IsRejected()
    {
        // given: код погашен успешным подтверждением; получен resetToken.
        var seeded = B14Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: FullName,
            role: UserRoles.Student,
            groupId: null,
            password: B14Harness.TestUserPassword);
        using var client = B14Harness.Create(_factory);
        var code = await B14RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);

        using var first = await B14RecoveryHarness.ConfirmAsync(client, Email, code);
        Assert.True(
            first.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: первое подтверждение живого кода → 200, фактически " +
            $"{(int)first.StatusCode}: {await first.Content.ReadAsStringAsync()}");
        var body = await B14Assertions.ReadRootObjectAsync(first);
        Assert.True(
            body.TryGetProperty("resetToken", out var resetToken)
            && resetToken.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetToken.GetString()),
            "Предусловие кейса: успешный confirm обязан выдать непустой resetToken.");

        // when: повторный POST с тем же (уже погашенным) кодом.
        using var repeat = await B14RecoveryHarness.ConfirmAsync(client, Email, code);

        // then: 400 'Код восстановления не подходит' (одноразовость кода).
        Assert.True(
            repeat.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 на повторном подтверждении, фактически " +
            $"{(int)repeat.StatusCode}: {await repeat.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(repeat),
            B14RecoveryHarness.CodeRejectedMessage);
    }
}
