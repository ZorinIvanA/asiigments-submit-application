using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-034 «Регистрация: дубликат логина проверяется раньше email» (FR-006 AC
/// «Дубликат логина проверяется первым»; P0): given — существуют пользователь
/// с login 'stu' и email 'stu@example.com'; when — POST /auth/register
/// {fullName:'Ф И О', login:'STU', email:'stu@example.com', password:'Passw0rd!',
/// repeatPassword:'Passw0rd!'}; then — 409 'Пользователь с таким логином уже
/// существует' — конфликт email не сообщается (проверка login раньше).
/// </summary>
public sealed class Ts034_DuplicateLoginFirstTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts034_DuplicateLoginFirstTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_DuplicateLoginCaseInsensitive_ReturnsLoginConflictWithoutEmailCheck()
    {
        // given: существуют пользователь с login 'stu' и email 'stu@example.com'.
        B03UserSeed.AddStudent(_factory, login: "stu", email: "stu@example.com");
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.34.0.1");
        var studentsBefore = B03UserSeed.CountStudents(_factory);

        // when: регистрация с тем же логином в другом регистре и тем же email.
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Ф И О",
            login: "STU",
            email: "stu@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var after = kdf.Snapshot();

        // then: 409 CONFLICT_LOGIN — конфликт логина обнаружен первым, email даже
        // не проверяется (иначе текстом был бы конфликт email).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Conflict, "POST /api/v1/auth/register (дубликат логина 'STU' при существующем 'stu')");
        ResponseAssert.MessageIs(body.RootElement, "Пользователь с таким логином уже существует");

        // then: Δkdf=0 — KDF выполняется только при успешном создании (FR-004(б)).
        Assert.Equal(0, B03Kdf.TotalDelta(before, after));

        // then: новый пользователь не создан (состав хранилища не изменился).
        Assert.Equal(studentsBefore, B03UserSeed.CountStudents(_factory));
    }
}
