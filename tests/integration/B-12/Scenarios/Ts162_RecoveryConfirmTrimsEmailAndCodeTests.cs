using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-162 (P2, boundary; FR-013) «recovery/confirm: трим email и code перед сверкой».
/// given: у student01@example.com живой код C — значение известно тесту из записи
///        'EmailDev' (POST /auth/recovery/request + [DEV-EMAIL]-запись тестового
///        sink, IF-005); лимит recovery_request не исчерпан (свежий хост фикстуры,
///        один запрос, FR-004: 3/час); шов хранилища доступен
///        (ISecurityTokenRepository из DI, IF-015).
/// when:  POST /api/v1/auth/recovery/confirm {email:'  student01@example.com  ',
///        code:'  <C>  '} — оба значения с пробельным обрамлением.
/// then:  200 {resetToken: непустая строка} — email и code триммятся до сверки
///        (FR-013: «email и code триммятся»); код C погашен (usedAt≠null —
///        инспекция записи хранилища независимо от живости, шов зоны B-12).
/// </summary>
public sealed class Ts162_RecoveryConfirmTrimsEmailAndCodeTests(B12RecoveryWebAppFactory factory)
    : IClassFixture<B12RecoveryWebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string FullName = "Студент Первый";

    private readonly B12RecoveryWebAppFactory _factory = factory;

    [Fact]
    public async Task Confirm_WithWhitespaceFramedEmailAndCode_TrimmedBeforeMatch()
    {
        // given: студент student01@example.com создан DI-сидом (ADR-010) — владелец кода
        // (пароль кейсу не нужен; хэш — реальный IPasswordHasher хоста, IF-002).
        B12RecoveryHarness.SeedStudentWithPassword(
            _factory, Login, FullName, Email, B12RecoveryHarness.TestUserPassword);

        // given: живой код C — значение известно тесту из записи 'EmailDev' (IF-005).
        using var client = B12AuthSessions.CreateClient(_factory);
        var code = await B12RecoveryHarness.RequestLiveCodeAsync(_factory, client, Email);

        // given (шов хранилища): запись живого кода владельца — Id для инспекции гашения.
        var owner = _factory.Services.GetRequiredService<IUserRepository>().GetByEmail(Email)
            ?? throw new InvalidOperationException(
                "Предусловие кейса: владелец кода не найден в хранилище после сида.");
        var liveCode = _factory.Services.GetRequiredService<ISecurityTokenRepository>()
            .FindLiveForUser(owner.Id);
        Assert.NotNull(liveCode);

        // when: confirm с пробельным обрамлением ОБЕИХ значений.
        using var response = await B12RecoveryHarness.ConfirmAsync(
            client,
            $"  {Email}  ",
            $"  {code}  ");

        // then: 200 {resetToken: непустая строка} — трим до сверки выполнен
        // (без трима значения не совпали бы с хранимыми ci-email/хэшем кода).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 (email и code триммятся, FR-013), фактически " +
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await ApiAssert.ReadJsonAsync(response);
        Assert.True(
            body.TryGetProperty("resetToken", out var resetTokenProperty),
            "В теле 200 отсутствует ключ resetToken.");
        Assert.Equal(JsonValueKind.String, resetTokenProperty.ValueKind);
        Assert.False(
            string.IsNullOrWhiteSpace(resetTokenProperty.GetString()),
            "resetToken — непустая строка (кейс TS-162: {resetToken: <непустая строка>}).");

        // then: код C погашен — usedAt≠null (шов хранилища: запись по Id независимо
        // от живости; консолидированный контракт живую запись после гашения не отдаёт).
        var consumed = B12RecoveryCodeSeam.FindByIdIncludingUsed(_factory, liveCode!.Id);
        Assert.NotNull(consumed);
        Assert.True(
            consumed!.UsedAt is not null,
            "Код C обязан быть погашен успешным подтверждением: usedAt≠null (кейс TS-162).");
    }
}
