using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-036 «Регистрация: поле role из входа игнорируется» (FR-006 AC «Роль из входа
/// игнорируется»; P1; out_of_scope «Управление ролями/смена роли»): given — все
/// обязательные поля валидны; логин и email свободны; when — POST /auth/register
/// с дополнительным полем role:'teacher'; then — 201; создан student — role в
/// ответе 'student'; роль teacher через API недостижима.
/// </summary>
public sealed class Ts036_RoleIgnoredTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts036_RoleIgnoredTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_WithExtraRoleField_CreatesStudent()
    {
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.36.0.1");

        // when: валидное тело с дополнительным полем role:'teacher' («сырое» JSON —
        // лишнее поле не выражается типизированным контрактом FR-006).
        using var response = await B03RegisterApi.PostRawAsync(
            client,
            @"{""fullName"":""Ролевой Тест"",""login"":""b03-role-in-input"",""email"":""b03-role@example.com""," +
            @"""password"":""Passw0rd!"",""repeatPassword"":""Passw0rd!"",""role"":""teacher""}");

        // then: 201; создан студент — role='student' в ответе, попытка эскалации роли
        // через вход игнорирована.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Created, "POST /api/v1/auth/register (с полем role:'teacher')");
        Assert.Equal("b03-role-in-input", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("student", body.RootElement.GetProperty("role").GetString());
    }
}
