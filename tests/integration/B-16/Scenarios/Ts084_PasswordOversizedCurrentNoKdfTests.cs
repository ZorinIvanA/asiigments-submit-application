using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-084 «me/password: currentPassword длиннее 128 — отказ без KDF» (boundary,
/// FR-016 + FR-005, P0).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie, ADR-015)
///        с реальным хэшем пароля; счётчик KDF «сброшен» — измерение ДЕЛЬТАМИ
///        снимков IKdfCounter.Snapshot() вокруг измеряемого PUT (B16KdfProbe).
/// when:  PUT {currentPassword: строка 129 символов, password:'NewPass1!',
///        confirmPassword:'NewPass1!'}.
/// then:  400 'Неверный текущий пароль'; Δkdf=0 — проверка не проходит без
///        выполнения деривации (FR-016 AC «Сверхдлинный currentPassword — без
///        KDF»; IF-013: длина &gt;128 — тот же 400 WRONG_CURRENT_PASSWORD БЕЗ KDF).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts084_PasswordOversizedCurrentNoKdfTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts084";
    private const string Email = "ts084@x.ru";

    private readonly B16WebAppFactory _factory;

    public Ts084_PasswordOversizedCurrentNoKdfTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithOversizedCurrent_Returns400_WithoutAnyKdfOperation()
    {
        // given: пользователь с реальным хэшем; сессия минтована.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-084",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик KDF сброшен» — база Δkdf на момент перед PUT.
        var before = B16KdfProbe.Snapshot(_factory.Services);

        // when: PUT со сверхдлинным currentPassword (129 символов).
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = new string('a', 129),
            password = B16Harness.NewPassword,
            confirmPassword = B16Harness.NewPassword,
        });

        // then: 400 «Неверный текущий пароль» (тот же конверт, без errors-карты).
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);

        // then: Δkdf=0 — ни одной деривации (гейт длины ДО KDF).
        var after = B16KdfProbe.Snapshot(_factory.Services);
        var delta = B16KdfProbe.TotalDelta(before, after);
        Assert.True(
            delta == 0,
            $"Ожидался Δkdf=0 (сверхдлинный currentPassword — без KDF), фактически {delta}.");
    }
}
