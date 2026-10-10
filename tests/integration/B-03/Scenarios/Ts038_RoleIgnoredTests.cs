using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B03.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-038 «Регистрация: поле role во входе игнорируется» (negative, FR-006 AC
/// «Роль из входа игнорируется»; P1): given — все обязательные поля валидны;
/// логин и email свободны; when — POST с дополнительным полем role:'teacher'
/// («сырое» JSON-тело — <see cref="B03RegisterApi.PostRawAsync"/>: лишнее поле
/// не выражается типизированным контрактом FR-006); then — 201; создан student
/// (роль в хранилище UserRoles.Student); role в MeDto = 'student' — попытка
/// эскалации роли через вход игнорирована.
/// </summary>
public sealed class Ts038_RoleIgnoredTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts038_RoleIgnoredTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_WithExtraRoleField_CreatesStudent()
    {
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.146.0.1");

        // when: валидное тело с дополнительным полем role:'teacher'.
        using var response = await B03RegisterApi.PostRawAsync(
            client,
            @"{""fullName"":""Ролевой Тест"",""login"":""ts038-role-input"",""email"":""ts038-role-input@example.com""," +
            @"""password"":""Passw0rd!"",""repeatPassword"":""Passw0rd!"",""role"":""teacher""}");

        // then: 201; создан student — роль в хранилище 'student', role в MeDto
        // тоже 'student' (role='student' всегда; поле role во входе игнорируется).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Created, "POST /api/v1/auth/register (с полем role:'teacher')");
        Assert.Equal("ts038-role-input", body.RootElement.GetProperty("login").GetString());
        Assert.Equal("student", body.RootElement.GetProperty("role").GetString());

        var created = B03UserSeed.FindByLogin(_factory, "ts038-role-input");
        Assert.NotNull(created);
        Assert.Equal(UserRoles.Student, created!.Role);
    }
}
