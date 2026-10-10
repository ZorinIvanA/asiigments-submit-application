using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-035 «Регистрация: дубликат email без учёта регистра» (FR-006 AC «Дубликат
/// email»; P0): given — существует пользователь с email 'a@b.ru'; when — POST
/// с новым свободным логином и email 'A@B.RU' (прочие поля валидны); then —
/// 409 'Пользователь с таким email уже существует'.
/// </summary>
public sealed class Ts035_DuplicateEmailTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts035_DuplicateEmailTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_DuplicateEmailOtherCase_ReturnsEmailConflict()
    {
        // given: существует пользователь с email 'a@b.ru'; логин запроса свободен.
        B03UserSeed.AddStudent(_factory, login: "ab-owner", email: "a@b.ru");
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.35.0.1");
        var studentsBefore = B03UserSeed.CountStudents(_factory);

        // when: регистрация с новым логином и email 'A@B.RU' (тот же email в другом регистре).
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Дубликат Эмеил",
            login: "email-case-check",
            email: "A@B.RU",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var after = kdf.Snapshot();

        // then: 409 CONFLICT_EMAIL.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Conflict, "POST /api/v1/auth/register (email 'A@B.RU' при существующем 'a@b.ru')");
        ResponseAssert.MessageIs(body.RootElement, "Пользователь с таким email уже существует");

        // then: Δkdf=0 — KDF выполняется только при успешном создании (FR-004(б)).
        Assert.Equal(0, B03Kdf.TotalDelta(before, after));

        // then: пользователь с новым логином не создан.
        Assert.Equal(studentsBefore, B03UserSeed.CountStudents(_factory));
        Assert.Null(B03UserSeed.FindByLogin(_factory, "email-case-check"));
    }
}
