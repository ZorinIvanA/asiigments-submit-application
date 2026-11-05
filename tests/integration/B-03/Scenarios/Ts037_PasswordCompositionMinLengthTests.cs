using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-037 «Регистрация: правила состава пароля применяются при длине ≥8» (FR-006;
/// P1): given — логин/email свободны; прочие поля валидны; when — POST /auth/register
/// с password='aaaaaaaa' (ровно 8 символов, только буквы) и repeatPassword='aaaaaaaa';
/// then — 400; errors.password содержит 'Пароль должен содержать хотя бы одну цифру'
/// и 'Пароль должен содержать хотя бы один специальный знак' и НЕ содержит 'Пароль
/// должен содержать не менее 8 символов'. Эталон поведения — client-валидатор
/// passwordRules (src/client/app/shared/validation/validators.ts): digit/letter/special
/// проверяются при любой длине, min — только при &lt;8.
/// </summary>
public sealed class Ts037_PasswordCompositionMinLengthTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts037_PasswordCompositionMinLengthTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_EightLetterPassword_ReportsCompositionRulesButNotMinLength()
    {
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.37.0.1");

        // when: password длиной ровно 8, только буквы; повтор совпадает.
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Состав Пароля",
            login: "b03-pw-composition",
            email: "b03-pw-composition@example.com",
            password: "aaaaaaaa",
            repeatPassword: "aaaaaaaa");

        // then: 400; errors.password содержит digit и special, НЕ содержит min.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (password='aaaaaaaa')");

        var errors = default(JsonElement);
        var passwordErrors = default(JsonElement);
        Assert.True(
            body.RootElement.TryGetProperty("errors", out errors)
            && errors.TryGetProperty("password", out passwordErrors)
            && passwordErrors.ValueKind == JsonValueKind.Array,
            $"Ожидался массив errors.password в теле ответа, фактически: {body.RootElement.GetRawText()}");
        var actual = passwordErrors.EnumerateArray().Select(item => item.GetString()!).ToArray();

        Assert.Contains("Пароль должен содержать хотя бы одну цифру", actual);
        Assert.Contains("Пароль должен содержать хотя бы один специальный знак", actual);
        Assert.DoesNotContain("Пароль должен содержать не менее 8 символов", actual);
    }
}
