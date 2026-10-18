using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-093 «Смена пароля: сверхдлинный currentPassword — 400 без KDF»
/// (boundary, FR-016 + ISS-016, P0; нумерация текущего батча B-16).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie,
///        ADR-015); счётчик обнулён — измерение ДЕЛЬТАМИ снимков
///        IKdfCounter.Snapshot() вокруг измеряемого PUT (B16KdfProbe, IF-002).
/// when:  PUT /me/password {currentPassword: строка 129 символов,
///        password:'NewPass1!', confirmPassword:'NewPass1!'}.
/// then:  400 'Неверный текущий пароль'; Δkdf=0 — проверка не выполняет
///        деривацию (AC FR-016 «Сверхдлинный currentPassword — без KDF»;
///        IF-013: длина &gt;128 — тот же 400 БЕЗ KDF).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts093_PasswordOversizedCurrentNoKdfTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts093pwd";
    private const string Email = "ts093pwd@x.ru";

    private readonly B16WebAppFactory _factory;

    public Ts093_PasswordOversizedCurrentNoKdfTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithOversizedCurrent_Returns400_AndPerformsZeroKdf()
    {
        // given: пользователь авторизован.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-093",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: «счётчик обнулён» — база Δkdf ровно вокруг PUT.
        var before = B16KdfProbe.Snapshot(_factory.Services);

        // when: PUT со сверхдлинным currentPassword (129 символов).
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = new string('a', 129),
            password = B16Harness.NewPassword,
            confirmPassword = B16Harness.NewPassword,
        });

        // then: 400 'Неверный текущий пароль'.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);

        // then: Δkdf=0 — проверка не выполняет деривацию.
        var after = B16KdfProbe.Snapshot(_factory.Services);
        var delta = B16KdfProbe.TotalDelta(before, after);
        Assert.True(
            delta == 0,
            $"Ожидался Δkdf=0 (сверхдлинный currentPassword — без KDF), фактически {delta}.");
    }
}
