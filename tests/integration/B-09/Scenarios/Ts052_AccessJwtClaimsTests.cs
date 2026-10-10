using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-052 «Токены: клеймы access-JWT» (happy_path, FR-008, P0).
///
/// given: выпущенный access-токен; ключ подписи теста известен
///        (Auth__JwtKey фиксирован в ApiFactory — фикстура зоны).
/// when:  декодирование payload JWT без верификации.
/// then:  sub = uuid пользователя; role ∈ {student, teacher}; exp−iat = 900
///        (AC FR-008 «Access-клеймы»).
///
/// Роль проверяется на ОБЕИХ границах множества {student, teacher}: вход
/// учителя (сид) и вход студента (регистрация). Файл текущей волны батча
/// B-09 (перенумерация кейсов): файл прежней волны (Ts049_AccessTokenClaims)
/// не изменялся.
/// </summary>
public sealed class Ts052_AccessJwtClaimsTests(B09TimedWebAppFactory factory) : IClassFixture<B09TimedWebAppFactory>
{
    private static readonly string[] AllowedRoles = ["student", "teacher"];

    private const string StudentLogin = "ts052student";
    private const string StudentEmail = "ts052student@example.com";
    private const string RegistrationIp = "10.0.0.52";
    private const string TeacherIp = "10.0.0.153";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task AccessJwtPayload_SubIsUserUuid_RoleWithinStudentTeacher_ExpMinusIat900()
    {
        // given: учитель существует (сид фикстуры); студент создан регистрацией;
        // выпущены access-токены обоих (успешные входы).
        _ = _factory.Services;
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var teacher = users.GetByLogin("teacher");
        Assert.NotNull(teacher);

        await B09HostClients.RegisterStudentAsync(
            _factory, "Пятьдесят Второй Студент", StudentLogin, StudentEmail, ip: RegistrationIp);
        var student = users.GetByLogin(StudentLogin);
        Assert.NotNull(student);

        using var teacherClient = B09AuthHttp.Create(_factory, TeacherIp);
        using var teacherLogin = await B09AuthHttp.LoginAsync(teacherClient, "teacher", B09AuthHttp.TeacherPassword);
        _ = await B09Assertions.ParseObjectAsync(teacherLogin, HttpStatusCode.OK, "вход учителя (TS-052)");
        var teacherToken = B09AuthSupport.SingleCookie(teacherLogin, "access_token", "Set-Cookie входа учителя");

        using var studentClient = B09AuthHttp.Create(_factory, TeacherIp);
        using var studentLogin = await B09AuthHttp.LoginAsync(studentClient, StudentLogin, B09HostClients.TestUserPassword);
        _ = await B09Assertions.ParseObjectAsync(studentLogin, HttpStatusCode.OK, "вход студента (TS-052)");
        var studentToken = B09AuthSupport.SingleCookie(studentLogin, "access_token", "Set-Cookie входа студента");

        // when: декодирование payload JWT без верификации.
        var teacherPayload = B09AuthSupport.DecodeJwtPayload(teacherToken.Value);
        var studentPayload = B09AuthSupport.DecodeJwtPayload(studentToken.Value);

        // then: sub = uuid пользователя.
        Assert.Equal(teacher.Id.ToString("D"), teacherPayload.GetProperty("sub").GetString());
        Assert.Equal(student.Id.ToString("D"), studentPayload.GetProperty("sub").GetString());

        // then: role ∈ {student, teacher} (границы множества: teacher и student).
        Assert.Contains(teacherPayload.GetProperty("role").GetString(), AllowedRoles);
        Assert.Contains(studentPayload.GetProperty("role").GetString(), AllowedRoles);
        Assert.Equal("teacher", teacherPayload.GetProperty("role").GetString());
        Assert.Equal("student", studentPayload.GetProperty("role").GetString());

        // then: exp−iat = 900.
        Assert.Equal(900, TokenLifetimeSeconds(teacherPayload));
        Assert.Equal(900, TokenLifetimeSeconds(studentPayload));
    }

    private static long TokenLifetimeSeconds(JsonElement payload)
    {
        var iat = payload.GetProperty("iat").GetInt64();
        var exp = payload.GetProperty("exp").GetInt64();
        return exp - iat;
    }
}
