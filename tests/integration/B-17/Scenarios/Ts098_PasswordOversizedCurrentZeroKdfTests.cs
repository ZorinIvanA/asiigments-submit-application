using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-098 «Смена пароля: currentPassword длиннее 128 — отказ без KDF»
/// (boundary, FR-016 + FR-005, P0).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie,
///        ADR-015); счётчик KDF «обнулён» — измерение ДЕЛЬТАМИ снимков
///        IKdfCounter.Snapshot() вокруг измеряемого PUT.
/// when:  PUT /me/password {currentPassword: строка 129 символов,
///        password:'NewPass1!', confirmPassword:'NewPass1!'}.
/// then:  400 'Неверный текущий пароль'; Δkdf=0 — проверка не проходит без
///        выполнения деривации (FR-016 AC «Сверхдлинный currentPassword — без
///        KDF»; IF-013: длина &gt;128 — тот же 400 WRONG_CURRENT_PASSWORD БЕЗ KDF).
/// </summary>
public sealed class Ts098_PasswordOversizedCurrentZeroKdfTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts098";
    private const string Email = "ts098@x.ru";

    private readonly B17WebAppFactory _factory;

    public Ts098_PasswordOversizedCurrentZeroKdfTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_With129CharCurrent_Returns400_WithoutAnyKdfOperation()
    {
        // given: пользователь с реальным хэшем; сессия минтована.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-098",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик KDF обнулён» — база Δkdf на момент перед PUT.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when: PUT со сверхдлинным currentPassword (129 символов).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = new string('a', 129),
            password = B17ProfileHost.NewPassword,
            confirmPassword = B17ProfileHost.NewPassword,
        });

        // then: 400 «Неверный текущий пароль».
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);

        // then: Δkdf=0 — ни одной деривации (гейт длины ДО KDF).
        var after = B17ProfileHost.KdfSnapshot(_factory);
        var delta = B17ProfileHost.KdfTotalDelta(before, after);
        Assert.True(
            delta == 0,
            $"Ожидался Δkdf=0 (сверхдлинный currentPassword — без KDF), фактически {delta}.");
    }
}
