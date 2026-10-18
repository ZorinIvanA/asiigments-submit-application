using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-093 «Смена пароля: неверный текущий + невалидный новый — WRONG_CURRENT_PASSWORD
/// первичен, без errors» (negative, FR-016, P0).
///
/// given: пользователь авторизован (валидная access-cookie); хранимый passwordHash
///        зафиксирован (снимок репозитория до запроса); счётчик KDF обнулён —
///        ДЕЛЬТАМИ снимков IKdfCounter.Snapshot() вокруг измеряемого PUT.
/// when:  PUT /api/v1/me/password {currentPassword:'wrong', password:'abc',
///        confirmPassword:'abc'} — неверный текущий пароль ВМЕСТЕ с невалидным новым.
/// then:  400; message «Неверный текущий пароль»; поля errors в теле НЕТ — полевая
///        валидация нового пароля НЕ выполнялась (появление errors.password означало
///        бы проверку полевых ошибок ДО проверки currentPassword — нарушение
///        порядка); хранимый passwordHash неизменен; Δkdf=1 — ровно одна деривация
///        Verify текущего пароля, деривации нового нет (FR-016(1); арбитраж
///        a-039/CR-001: дискриминирующая комбинация wrong + weak делает пункт AC
///        «новый пароль НЕ валидировался» детектируемым).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts093_PasswordWrongCurrentNoFieldValidationTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts093";
    private const string Email = "ts093@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts093_PasswordWrongCurrentNoFieldValidationTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WrongCurrentWithWeakNew_Returns400WithoutErrors_OneKdf_HashKept()
    {
        // given: пользователь с реальным PBKDF2-хэшем; сессия минтована.
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-093",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // given: хранимый passwordHash зафиксирован (снимок репозитория до запроса);
        // «счётчик KDF обнулён» — база Δkdf ровно вокруг PUT.
        var storedBefore = B18ProfileHarness.StoredUser(_factory, user.Id);
        var before = B18ProfileKdfProbe.Snapshot(_factory.Services);

        // when: НЕВЕРНЫЙ currentPassword вместе с НЕВАЛИДНЫМ новым ('abc' — короче
        // 8 символов, без цифры и спецзнака).
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = "wrong",
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 «Неверный текущий пароль» БЕЗ errors-карты — полевая валидация
        // нового пароля не выполнялась (проверка currentPassword ДО полевых ошибок).
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.MessageIs(root, ErrorTexts.WrongCurrentPassword);
        B18ProfileAssertions.ErrorsPropertyIsAbsent(root);

        // then: Δkdf=1 — ровно одна деривация (Verify текущего пароля), деривации
        // нового (hash) нет.
        var after = B18ProfileKdfProbe.Snapshot(_factory.Services);
        var delta = B18ProfileKdfProbe.TotalDelta(before, after);
        Assert.True(
            delta == 1,
            $"Ожидался Δkdf=1 (только Verify текущего пароля, без деривации нового), фактически {delta}.");

        // then: хранимый passwordHash неизменен (снимок после запроса совпадает).
        var storedAfter = B18ProfileHarness.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(storedAfter.PasswordHash, storedBefore.PasswordHash, StringComparison.Ordinal),
            "Ожидался неизменный хранимый passwordHash (неверный currentPassword — до каких-либо " +
            "изменений), фактически хэш изменился.");
    }
}
