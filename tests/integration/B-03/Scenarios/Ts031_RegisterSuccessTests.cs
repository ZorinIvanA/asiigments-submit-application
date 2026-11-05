using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-031 «Регистрация: успех, MeDto, cookie, ровно один KDF» (FR-006 AC «Успешная
/// регистрация», FR-008; P0): given — логин 'newuser' и email 'nu@example.com' свободны
/// (Seed__DemoData=false), регистрационный лимит не исчерпан (свой IP кейса, FR-004),
/// счётчик KDF сброшен (снимок до запроса); when — POST /api/v1/auth/register
/// {fullName:'Иванов Иван', login:'newuser', email:'nu@example.com',
/// password:'Passw0rd!', repeatPassword:'Passw0rd!'}; then — 201; тело MeDto
/// {login:'newuser', fullName:'Иванов Иван', role:'student', groupName:null}; в ответе
/// Set-Cookie access_token и refresh_token; Δkdf(register)=1.
/// </summary>
public sealed class Ts031_RegisterSuccessTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts031_RegisterSuccessTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_ValidBody_Returns201MeDtoTwoCookiesAndSingleRegisterKdf()
    {
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.31.0.1");

        // when: POST /api/v1/auth/register с валидными полями; Δkdf — между снимками.
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Иванов Иван",
            login: "newuser",
            email: "nu@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var after = kdf.Snapshot();

        // then: 201; тело MeDto с role='student' и groupName=null.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Created, "POST /api/v1/auth/register (валидные поля)");
        Assert.Equal("newuser", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("student", body.RootElement.GetProperty("role").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            body.RootElement.GetProperty("groupName").ValueKind);

        // then: в ответе Set-Cookie access_token и refresh_token (FR-008).
        var setCookies = ManualCookies.GetSetCookie(response);
        Assert.True(
            setCookies.Count == 2,
            $"Ожидались две Set-Cookie (access_token и refresh_token), фактически {setCookies.Count}: {string.Join(" | ", setCookies)}");
        Assert.Contains(setCookies, cookie => cookie.StartsWith("access_token=", StringComparison.Ordinal));
        Assert.Contains(setCookies, cookie => cookie.StartsWith("refresh_token=", StringComparison.Ordinal));

        // then: Δkdf(register)=1 — пароль хэшируется ровно один раз при создании (FR-006);
        // суммарный Δkdf по всем меткам тоже 1 — на успех-пути нет дериваций под иными
        // метками (FR-004(б): иные неаутентифицированные KDF-поверхности запрещены).
        Assert.Equal(
            1,
            B03Kdf.CallerDelta(before, after, B03Kdf.RegisterCaller));
        Assert.Equal(1, B03Kdf.TotalDelta(before, after));
    }
}
