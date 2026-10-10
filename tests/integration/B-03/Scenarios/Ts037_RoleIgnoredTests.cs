using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-037 «Регистрация: поле role во входе игнорируется» (FR-006 AC «Роль из входа
/// игнорируется»; P1): given — все обязательные поля валидны; логин и email свободны;
/// when — POST с дополнительным полем role:'teacher'; then — создан student; роль
/// в ответе 'student' (role='student' всегда; поле role во входе при наличии
/// игнорируется).
/// </summary>
public sealed class Ts037_RoleIgnoredTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts037_RoleIgnoredTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_WithExtraRoleField_CreatesStudent()
    {
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.37.0.1");

        // when: валидное тело с дополнительным полем role:'teacher' («сырое» JSON —
        // лишнее поле не выражается типизированным контрактом FR-006).
        using var response = await B03RegisterApi.PostRawAsync(
            client,
            @"{""fullName"":""Ролевой Тест"",""login"":""role-in-input"",""email"":""role-in-input@example.com""," +
            @"""password"":""Passw0rd!"",""repeatPassword"":""Passw0rd!"",""role"":""teacher""}");

        // then: 201; создан студент — role='student' в ответе, попытка эскалации роли
        // через вход игнорирована.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Created, "POST /api/v1/auth/register (с полем role:'teacher')");
        Assert.Equal("role-in-input", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("student", body.RootElement.GetProperty("role").GetString());
    }
}
