using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-032 «Регистрация: успех 201 + MeDto + cookie + 1 KDF» (happy_path, FR-006
/// AC «Успешная регистрация», FR-008; P0): given — тестовый хост (Development,
/// Auth__Pbkdf2Iterations=1000 — <see cref="B03HostFactory"/>, чистое состояние —
/// Seed__DemoData=false); пользователя с логином 'newuser' нет; счётчик KDF
/// обнулён (Δ считается между снимками до/после запроса — <see cref="B03Kdf"/>);
/// when — POST /api/v1/auth/register {fullName:'Иванов Иван', login:'newuser',
/// email:'nu@example.com', password:'Passw0rd!', repeatPassword:'Passw0rd!'};
/// then — 201; тело MeDto {login:'newuser', fullName:'Иванов Иван', role:'student',
/// groupName:null}; Set-Cookie access_token и refresh_token; Δkdf по метке
/// 'register' = 1 (FR-006 AC «Успешная регистрация»).
/// </summary>
public sealed class Ts032_RegisterSuccessMeDtoTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts032_RegisterSuccessMeDtoTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_NewUser_Returns201MeDtoCookiesAndSingleRegisterKdf()
    {
        // given: пользователя 'newuser' нет; счётчик KDF зафиксирован снимком.
        Assert.Null(B03UserSeed.FindByLogin(_factory, "newuser"));
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.140.0.1");

        // when: POST /api/v1/auth/register с валидными полями.
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Иванов Иван",
            login: "newuser",
            email: "nu@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var after = kdf.Snapshot();

        // then: 201; тело MeDto {login, fullName, role:'student', groupName:null}.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Created, "POST /api/v1/auth/register (валидные поля)");
        Assert.Equal("newuser", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("Иванов Иван", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("student", body.RootElement.GetProperty("role").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            body.RootElement.GetProperty("groupName").ValueKind);

        // then: Set-Cookie access_token и refresh_token (FR-008; cookie-контейнер
        // клиента отключён — заголовки инспектируются вручную, <see cref="ManualCookies"/>).
        var setCookies = ManualCookies.GetSetCookie(response);
        Assert.True(
            setCookies.Count == 2,
            $"Ожидались две Set-Cookie (access_token и refresh_token), фактически {setCookies.Count}: {string.Join(" | ", setCookies)}");
        Assert.Contains(setCookies, cookie => cookie.StartsWith("access_token=", StringComparison.Ordinal));
        Assert.Contains(setCookies, cookie => cookie.StartsWith("refresh_token=", StringComparison.Ordinal));

        // then: Δkdf по метке 'register' = 1 (пароль хэшируется ровно один раз при
        // создании, FR-006); суммарный Δkdf по всем меткам тоже 1 — на успех-пути
        // нет дериваций под иными метками (FR-004(б)).
        Assert.Equal(1, B03Kdf.CallerDelta(before, after, B03Kdf.RegisterCaller));
        Assert.Equal(1, B03Kdf.TotalDelta(before, after));
    }
}
