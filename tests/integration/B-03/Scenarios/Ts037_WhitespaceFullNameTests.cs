using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-037 «Регистрация: пробельное ФИО невалидно» (negative, FR-006 AC
/// «Пробельное ФИО невалидно»; P1): given — все прочие поля валидны; when —
/// POST с fullName='   ' (триммируемое поле из одних пробелов); then — 400,
/// errors.fullName=['Заполните поле'].
/// </summary>
public sealed class Ts037_WhitespaceFullNameTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts037_WhitespaceFullNameTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_WhitespaceOnlyFullName_RequiresField()
    {
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.145.0.1");

        // when: fullName — только пробелы, остальные поля валидны.
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "   ",
            login: "ts037-ws-fullname",
            email: "ts037-ws-fullname@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 400 с required-текстом по fullName.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (fullName из одних пробелов)");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "fullName", "Заполните поле");
    }
}
