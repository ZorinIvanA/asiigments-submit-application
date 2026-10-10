using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-162 «recovery/confirm: трим email и code перед сверкой» (boundary, FR-013, P1).
///
/// given: студент student01@example.com создан DI-сидом; живой код C получен
///        собственным POST /auth/recovery/request, значение известно тесту из
///        [DEV-EMAIL]-записи категории 'EmailDev' тестового sink (IF-005);
///        лимит recovery_request не исчерпан (свежий хост класса, одна попытка);
///        шов хранилища доступен (FindLiveForUser, IF-015);
/// when:  POST /api/v1/auth/recovery/confirm {email:'  student01@example.com  ',
///        code:'  {C}  '} — оба значения с пробельным обрамлением;
/// then:  200 {resetToken: непустая строка} — email и code триммятся до сверки
///        (FR-013: «email и code триммятся»); код C погашен (usedAt≠null —
///        живых кодов владельца после успешного подтверждения нет).
/// </summary>
public sealed class Ts162_RecoveryConfirmTrimsEmailAndCodeTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts162_RecoveryConfirmTrimsEmailAndCodeTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_WithWhitespaceFramedEmailAndCode_TrimsBoth_AndConfirms()
    {
        // given: пользователь DI-сидом; живой код C — из [DEV-EMAIL]-записи ('EmailDev').
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

        // when: confirm с пробельным обрамлением email и code.
        using var confirm = await B14RecoveryHarness.ConfirmAsync(
            client,
            $"  {Email}  ",
            $"  {code}  ");

        // then: 200 {resetToken: непустая строка} — триммированные значения совпали
        // с ключом пользователя и хэшем кода (FR-013: «email и code триммятся»).
        Assert.True(
            confirm.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 на триммированных значениях, фактически " +
            $"{(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        var body = await B14Assertions.ReadRootObjectAsync(confirm);
        Assert.True(
            body.TryGetProperty("resetToken", out var resetTokenProperty)
            && resetTokenProperty.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "Ожидался непустой resetToken в теле 200 (FR-013: email и code триммятся до сверки).");

        // then: код C погашен (usedAt≠null) — живых кодов владельца больше нет.
        Assert.True(
            B14RecoveryCodeInspection.FindLiveRecoveryCode(_factory.Services, seeded.Id) is null,
            "Код C обязан быть погашен успешным подтверждением (usedAt≠null): " +
            "живых кодов владельца быть не должно.");
    }
}
