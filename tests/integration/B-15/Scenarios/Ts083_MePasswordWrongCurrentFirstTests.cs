using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-083 «me/password: неверный текущий пароль проверяется до валидации нового»
/// (negative, FR-016, P0).
///
/// given: пользователь авторизован (сессия харнеса, ADR-015/CR-001) с РЕАЛЬНЫМ
///        паролем 'OldPass1!' (DI-сид через IPasswordHasher, метка seed);
///        счётчик KDF измеряется дельтами B15KdfProbe (given «сброшен»).
/// when:  PUT /api/v1/me/password {currentPassword:'wrong', password:'abc',
///        confirmPassword:'abc'} — новый пароль заведомо невалиден.
/// then:  400, message 'Неверный текущий пароль', БЕЗ errors-карты (появление
///        errors.password означало бы обратный порядок проверок); новый пароль
///        НЕ валидировался (Δkdf change_password = 1 — только Verify текущего)
///        и не применён (passwordHash в хранилище не изменился) —
///        FR-016 AC «Неверный текущий пароль», IF-013 WRONG_CURRENT_PASSWORD.
/// </summary>
public sealed class Ts083_MePasswordWrongCurrentFirstTests : IClassFixture<B15PasswordWebAppFactory>
{
    private const string Login = "ts083user";
    private const string Email = "ts083@example.com";
    private const string FullName = "Пользователь ВосьмдесятТри";
    private const string RealPassword = "OldPass1!";

    private readonly B15PasswordWebAppFactory _factory;

    public Ts083_MePasswordWrongCurrentFirstTests(B15PasswordWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WrongCurrent_ReturnsMessageOnly_AndSkipsNewValidation()
    {
        // given: пользователь с реальным паролем и сессией.
        var user = B15PasswordSessions.SeedUserWithPassword(
            _factory, Login, Email, FullName, RealPassword);
        using var client = B15PasswordSessions.CreateSessionClient(_factory, user.Id);
        var storedHashBefore = B15Harness.UserById(_factory, user.Id).PasswordHash;

        var before = B15KdfProbe.Snapshot(_factory.Services);

        // when: неверный currentPassword при заведомо невалидном новом пароле.
        using var response = await client.PutAsJsonAsync(B15PasswordSessions.MePasswordEndpoint, new
        {
            currentPassword = "wrong",
            password = "abc",
            confirmPassword = "abc",
        });

        // then: 400 WRONG_CURRENT_PASSWORD — одно сообщение, без errors-карты.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Неверный текущий пароль");
        Assert.False(
            envelope.TryGetProperty("errors", out _),
            "400 неверного текущего пароля не должен содержать errors-карты — она означала бы, " +
            "что новый пароль валидировался раньше проверки currentPassword (FR-016).");

        // Δkdf(change_password) = 1: ровно одна деривация Verify текущего пароля,
        // полевая валидация нового не выполнялась.
        var after = B15KdfProbe.Snapshot(_factory.Services);
        Assert.Equal(1, B15KdfProbe.CallerDelta(before, after, "change_password"));

        // Новый пароль не применён: хэш в хранилище не изменился.
        Assert.True(
            string.Equals(storedHashBefore, B15Harness.UserById(_factory, user.Id).PasswordHash, StringComparison.Ordinal),
            "passwordHash пользователя изменился — новый пароль был применён вопреки отказу (FR-016).");
    }
}
