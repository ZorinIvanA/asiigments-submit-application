using LabsApp.IntegrationTests.B14.Infrastructure;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// TS-076 «recovery/confirm: незарегистрированный email — тот же текст»
/// (negative, FR-013, P0).
///
/// given: пользователя с email 'no@no.no' не существует (свежий хост фикстуры —
///        хранилище пусто, никакие пользователи не сидируются);
/// when:  POST /api/v1/auth/recovery/confirm {email:'no@no.no', code:'123456'};
/// then:  400; message 'Код восстановления не подходит' — ТОТ ЖЕ текст, что при
///        неверном коде живому пользователю (без раскрытия существования
///        аккаунта; FR-013 AC «Незарегистрированный email»).
/// </summary>
public sealed class Ts076_RecoveryConfirmUnknownEmailTests : IClassFixture<B14RecoveryWebAppFactory>
{
    private const string UnknownEmail = "no@no.no";
    private const string Code = "123456";

    private readonly B14RecoveryWebAppFactory _factory;

    public Ts076_RecoveryConfirmUnknownEmailTests(B14RecoveryWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_ForUnknownEmail_ReturnsSameCodeRejectedMessage()
    {
        // given: пользователя с email 'no@no.no' не существует — хост фикстуры
        // не сидирует никаких пользователей сверх сид-преподавателя.

        // when: подтверждение кода для незарегистрированного email.
        using var client = B14Harness.Create(_factory);
        using var confirm = await B14RecoveryHarness.ConfirmAsync(client, UnknownEmail, Code);

        // then: 400; message — тот же текст CODE_REJECTED (оракула существования нет).
        Assert.True(
            confirm.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)confirm.StatusCode}: {await confirm.Content.ReadAsStringAsync()}");
        B14Assertions.MessageIs(
            await B14Assertions.ReadRootObjectAsync(confirm),
            B14RecoveryHarness.CodeRejectedMessage);
    }
}
