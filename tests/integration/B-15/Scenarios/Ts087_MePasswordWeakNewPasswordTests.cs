using LabsApp.IntegrationTests.B15.Infrastructure;
using System.Text.Json;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-087 «me/password: слабый новый пароль — пакет ошибок» (negative,
/// FR-016 + FR-006, P0).
///
/// given: currentPassword совпадает с текущим паролем пользователя ('OldPass1!',
///        DI-сид через IPasswordHasher, метка seed).
/// when:  PUT {currentPassword:'OldPass1!', password:'abc', confirmPassword:'abc'}.
/// then:  400 'Данные заполнены неверно' + errors.password — ровно три текста
///        словаря FR-006 (min/digit/special; буква есть, поэтому password.letter
///        не срабатывает); confirmPassword равен паролю — поле в errors
///        не появляется (FR-016 AC «Слабый новый пароль»).
/// </summary>
public sealed class Ts087_MePasswordWeakNewPasswordTests : IClassFixture<B15PasswordWebAppFactory>
{
    private const string Login = "ts087user";
    private const string Email = "ts087@example.com";
    private const string FullName = "Пользователь ВосемьдесятСемь";
    private const string RealPassword = "OldPass1!";
    private const string WeakPassword = "abc";

    private readonly B15PasswordWebAppFactory _factory;

    public Ts087_MePasswordWeakNewPasswordTests(B15PasswordWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WeakNewPassword_ReturnsFieldErrorPackage()
    {
        // given: пользователь, чей currentPassword совпадает с реальным паролем.
        var user = B15PasswordSessions.SeedUserWithPassword(
            _factory, Login, Email, FullName, RealPassword);
        using var client = B15PasswordSessions.CreateSessionClient(_factory, user.Id);

        // when: смена на слабый пароль при верном currentPassword.
        using var response = await client.PutAsJsonAsync(B15PasswordSessions.MePasswordEndpoint, new
        {
            currentPassword = RealPassword,
            password = WeakPassword,
            confirmPassword = WeakPassword,
        });

        // then: 400 VALIDATION с полным пакетом нарушенных правил нового пароля.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(
            envelope,
            "password",
            "Пароль должен содержать не менее 8 символов",
            "Пароль должен содержать хотя бы одну цифру",
            "Пароль должен содержать хотя бы один специальный знак");

        // confirmPassword дословно равен паролю — поля confirmPassword в errors нет.
        Assert.True(envelope.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.False(
            errors.TryGetProperty("confirmPassword", out _),
            "errors.confirmPassword не должен появляться при дословно равном повторе (FR-006).");
    }
}
