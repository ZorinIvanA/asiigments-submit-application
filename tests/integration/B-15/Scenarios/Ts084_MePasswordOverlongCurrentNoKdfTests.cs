using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-084 «me/password: currentPassword длиннее 128 — отказ без KDF»
/// (boundary, FR-016, P0).
///
/// given: пользователь авторизован с реальным паролем (DI-сид, метка seed);
///        счётчик KDF измеряется дельтами B15KdfProbe (given «сброшен»).
/// when:  PUT {currentPassword: &lt;строка 129 символов&gt;, password:'NewPass1!',
///        confirmPassword:'NewPass1!'}.
/// then:  400 'Неверный текущий пароль'; Δkdf = 0 суммарно по всем меткам —
///        проверка не проходит без выполнения деривации
///        (FR-016 AC «Сверхдлинный currentPassword — без KDF», IF-013).
/// </summary>
public sealed class Ts084_MePasswordOverlongCurrentNoKdfTests : IClassFixture<B15PasswordWebAppFactory>
{
    private const string Login = "ts084user";
    private const string Email = "ts084@example.com";
    private const string FullName = "Пользователь ВосьмдесятЧетыре";
    private const string RealPassword = "OldPass1!";
    private const int OverlongLength = 129;

    private readonly B15PasswordWebAppFactory _factory;

    public Ts084_MePasswordOverlongCurrentNoKdfTests(B15PasswordWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_OverlongCurrent_RejectsWithoutAnyDerivation()
    {
        // given: пользователь с реальным паролем и сессией.
        var user = B15PasswordSessions.SeedUserWithPassword(
            _factory, Login, Email, FullName, RealPassword);
        using var client = B15PasswordSessions.CreateSessionClient(_factory, user.Id);

        var before = B15KdfProbe.Snapshot(_factory.Services);

        // when: currentPassword длиной 129 символов (>128) при валидном новом.
        using var response = await client.PutAsJsonAsync(B15PasswordSessions.MePasswordEndpoint, new
        {
            currentPassword = new string('x', OverlongLength),
            password = "NewPass1!",
            confirmPassword = "NewPass1!",
        });

        // then: 400 WRONG_CURRENT_PASSWORD; Δkdf = 0 — гейт длины сработал
        // ДО любой деривации (суммарно по всем меткам ни одной деривации).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Неверный текущий пароль");

        var after = B15KdfProbe.Snapshot(_factory.Services);
        Assert.Equal(0, B15KdfProbe.TotalDelta(before, after));
    }
}
