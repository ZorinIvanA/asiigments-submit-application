using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-036 «Регистрация: пробельное ФИО невалидно» (FR-006 AC «Пробельное ФИО
/// невалидно»; P1): given — все прочие поля запроса валидны; when — POST
/// с fullName='   ' (строка из одних пробелов; триммируемое поле); then — 400,
/// errors.fullName=['Заполните поле'] (required-текст для пустого после трима поля).
/// </summary>
public sealed class Ts036_WhitespaceFullNameTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts036_WhitespaceFullNameTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_WhitespaceOnlyFullName_RequiresField()
    {
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.36.0.1");

        // when: fullName — только пробелы, остальные поля валидны.
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "   ",
            login: "ws-fullname",
            email: "ws-fullname@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 400 с required-текстом по fullName.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (fullName из одних пробелов)");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "fullName", "Заполните поле");
    }
}
