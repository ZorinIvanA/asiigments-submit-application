using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-102 «Смена пароля: новый пароль длиной 129 — 400 password.max»
/// (boundary, FR-016, P1).
///
/// given: currentPassword верный; confirmPassword равен password; счётчик KDF
///        «обнулён» — дельты снимков IKdfCounter.Snapshot(); сессия —
///        минтованный access-cookie (ADR-015).
/// when:  PUT /me/password с password длиной 129 (валидного состава:
///        буквы, цифра, спецзнак), confirmPassword — та же строка.
/// then:  400 'Данные заполнены неверно' +
///        errors.password=['Пароль — не более 128 символов']; Δkdf=1 (только
///        verify текущего — ISS-016: прочие правила при длине &gt;128 не
///        проверяются, хэширование нового не выполняется) (FR-016 + ISS-016).
/// </summary>
public sealed class Ts102_PasswordNewMax129OneKdfTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts102";
    private const string Email = "ts102@x.ru";

    /// <summary>Новый пароль длиной 129 валидного состава: буквы, цифра, спецзнак.</summary>
    private static string OversizedPassword129 => new string('a', 127) + "1!";

    private readonly B17WebAppFactory _factory;

    public Ts102_PasswordNewMax129OneKdfTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_With129CharNewPassword_Returns400WithSingleMaxError_OneKdf()
    {
        // given: пользователь; currentPassword верен; сессия минтована.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-102",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        var oversized = OversizedPassword129;
        Assert.Equal(129, oversized.Length);

        // given: «счётчик KDF обнулён» — база Δkdf.
        var before = B17ProfileHost.KdfSnapshot(_factory);

        // when: PUT с password-повтором длиной 129 (верхняя граница превышена).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = B17ProfileHost.TestUserPassword,
            password = oversized,
            confirmPassword = oversized,
        });

        // then: 400; errors.password = ['Пароль — не более 128 символов'] —
        // единственная ошибка пароля при длине >128.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "password", ErrorTexts.PasswordMax);

        // then: Δkdf=1 — только verify текущего пароля; хэширование нового
        // не выполнялось.
        var after = B17ProfileHost.KdfSnapshot(_factory);
        var delta = B17ProfileHost.KdfTotalDelta(before, after);
        Assert.True(
            delta == 1,
            $"Ожидался Δkdf=1 (только verify текущего), фактически {delta}.");
    }
}
