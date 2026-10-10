using System.Net;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// Кейс батча B-20: TS-186 «Scope: подтверждение email при регистрации
/// отсутствует» (scope, P2; OUT-SCOPE-EMAIL-CONFIRM).
/// given: Регистрация нового пользователя завершена (201, пароль 'Passw0rd!');
///        никаких шагов подтверждения не выполнялось.
/// when:  Немедленный POST /auth/login с теми же учётными данными.
/// then:  200 MeDto — вход работает сразу, подтверждение email не требуется
///        и не реализовано (out_of_scope: «Подтверждение email при
///        регистрации»).
/// </summary>
public sealed class Ts186_RegisterImmediateLoginScopeTests
{
    private const string FullName = "Студент Скоупа TS-186";
    private const string Login = "ts186-scope-student";
    private const string Email = "ts186-scope-student@example.com";
    private const string Password = "Passw0rd!";

    [Fact]
    public async Task Register201_ThenImmediateLogin_200MeDto_WithoutAnyConfirmationStep()
    {
        // given: приложение запущено; анонимный клиент (без cookie-контейнера).
        using var factory = new B20ApiFactory();
        using var client = B20AuthSessions.Create(factory);

        // given: регистрация нового пользователя завершена — 201 MeDto; НИКАКИХ
        // шагов подтверждения email между ней и входом не выполняется (их нет
        // в API — out_of_scope).
        using (var register = await client.PostAsync(
            "/api/v1/auth/register",
            JsonBody(
                $$"""
                {
                  "fullName": "{{FullName}}",
                  "login": "{{Login}}",
                  "email": "{{Email}}",
                  "password": "{{Password}}",
                  "repeatPassword": "{{Password}}"
                }
                """)))
        {
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            var registered = await ReadJsonObjectAsync(register);
            Assert.Equal(Login, registered.GetProperty("login").GetString());
            Assert.Equal(FullName, registered.GetProperty("fullName").GetString());
            Assert.Equal("student", registered.GetProperty("role").GetString());
        }

        // when: немедленный POST /auth/login с теми же учётными данными.
        using var login = await client.PostAsync(
            "/api/v1/auth/login",
            JsonBody(
                $$"""
                {
                  "login": "{{Login}}",
                  "password": "{{Password}}"
                }
                """));

        // then: 200 MeDto — вход работает сразу, подтверждение email не
        // требуется и не реализовано (out_of_scope: «Подтверждение email при
        // регистрации»).
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await ReadJsonObjectAsync(login);
        Assert.Equal(JsonValueKind.Object, me.ValueKind);
        Assert.Equal(Login, me.GetProperty("login").GetString());
        Assert.Equal(FullName, me.GetProperty("fullName").GetString());
        Assert.Equal("student", me.GetProperty("role").GetString());
    }

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJsonObjectAsync(HttpResponseMessage response)
    {
        var element = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        return element;
    }
}
