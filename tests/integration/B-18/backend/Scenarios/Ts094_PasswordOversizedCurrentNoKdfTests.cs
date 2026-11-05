using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-094 «Смена пароля: сверхдлинный currentPassword — без KDF» (boundary,
/// FR-016 + FR-005, P0).
///
/// given: пользователь авторизован; счётчик KDF обнулён — реализовано ДЕЛЬТАМИ
///        снимков IKdfCounter.Snapshot() до/после измеряемого PUT (B18ProfileKdfProbe).
/// when:  PUT /me/password {currentPassword: строка 129 символов, password:'NewPass1!',
///        confirmPassword:'NewPass1!'}.
/// then:  400; message «Неверный текущий пароль»; Δkdf=0 (FR-016 AC «Сверхдлинный
///        currentPassword — без KDF»; IF-013: длина &gt;128 — тот же 400
///        WRONG_CURRENT_PASSWORD БЕЗ выполнения KDF).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts094_PasswordOversizedCurrentNoKdfTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts094";
    private const string Email = "ts094@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts094_PasswordOversizedCurrentNoKdfTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithOversizedCurrent_Returns400_WithoutAnyKdfOperation()
    {
        // given: пользователь с реальным хэшем; сессия минтована.
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-094",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик KDF обнулён» — база Δkdf на момент перед PUT.
        var before = B18ProfileKdfProbe.Snapshot(_factory.Services);

        // when: PUT со сверхдлинным currentPassword (129 символов).
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = new string('a', 129),
            password = B18ProfileHarness.NewPassword,
            confirmPassword = B18ProfileHarness.NewPassword,
        });

        // then: 400 «Неверный текущий пароль» (тот же конверт, без errors-карты).
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);

        // then: Δkdf=0 — ни одной деривации (гейт длины ДО KDF).
        var after = B18ProfileKdfProbe.Snapshot(_factory.Services);
        var delta = B18ProfileKdfProbe.TotalDelta(before, after);
        Assert.True(
            delta == 0,
            $"Ожидался Δkdf=0 (сверхдлинный currentPassword — без KDF), фактически {delta}.");
    }
}
