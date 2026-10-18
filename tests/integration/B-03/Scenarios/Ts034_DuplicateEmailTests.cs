using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-034 «Регистрация: дубликат email без учёта регистра» (FR-006 AC «Дубликат
/// email»; P0): given — существует email 'a@b.ru' (DI-сид); новый логин свободен;
/// when — POST /auth/register с новым логином и email 'A@B.RU' (валидные прочие
/// поля); then — 409 'Пользователь с таким email уже существует' — уникальность
/// lower(email).
/// </summary>
public sealed class Ts034_DuplicateEmailTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts034_DuplicateEmailTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_DuplicateEmailOtherCase_ReturnsEmailConflict()
    {
        // given: существует пользователь с email 'a@b.ru'; логин запроса свободен.
        B03UserSeed.AddStudent(_factory, login: "ab-owner", email: "a@b.ru");
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.34.0.1");

        // when: регистрация с новым логином и email 'A@B.RU' (тот же email в другом регистре).
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Дубликат Эмеил",
            login: "b03-email-ci",
            email: "A@B.RU",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 409 'Пользователь с таким email уже существует' (uniqueness по lower(email)).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response,
            HttpStatusCode.Conflict,
            "POST /api/v1/auth/register (email 'A@B.RU' при существующем 'a@b.ru')");
        ResponseAssert.MessageIs(body.RootElement, "Пользователь с таким email уже существует");
    }
}
