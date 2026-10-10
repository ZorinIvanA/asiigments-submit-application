using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-154 «Словарь §8: все message воспроизводятся дословно» (nfr, NFR-007, FR-024).
///
/// given: свежий экземпляр приложения (Development, демо-сид); параметры для
///        триггеров каждого сообщения.
/// when:  параметрический контрактный прогон (по одному [Fact] на триггер):
///        «Неверный логин или пароль», «Данные заполнены неверно», «Пользователь
///        с таким логином уже существует», «Пользователь с таким email уже
///        существует», «Неверный текущий пароль», «Код восстановления не
///        подходит», «Ссылка восстановления недействительна или истекла»,
///        «Лабораторная с таким номером уже есть в семестре», «Лабораторная не
///        найдена», «Группа с таким названием уже существует», «Группа не
///        найдена», «Студент не найден», «Не авторизован», «Доступ запрещён»,
///        «Не найдено»; «Слишком много попыток. Повторите позже» — в отдельном
///        классе с собственной фикстурой (6 регистраций исчерпали бы общий
///        счётчик этого класса, влияя на триггеры 400/409 регистрации).
/// then:  для каждого триггера фактическое поле message совпадает с эталонной
///        строкой побайтно (включая пробелы, точки и регистр). NFR-007: «Все
///        пользовательские тексты message — дословно из словаря §8»; глоссарий
///        «Словарь ошибок API (§8)».
///
/// Бюджет регистраций общего экземпляра: 3 попытки (&lt; 5/час) — лимит не задет.
/// </summary>
public sealed class Ts154_DictionaryMessageTests : IClassFixture<B05WebAppFactory>
{
    private const string ZeroId = "00000000-0000-0000-0000-000000000000";

    private readonly B05WebAppFactory _factory;

    public Ts154_DictionaryMessageTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task InvalidCredentials_LoginWithWrongPassword()
    {
        // given: пользователь student01 существует (демо-сид).
        using var client = HostClients.Create(_factory);

        // when: вход с неверным паролем.
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login = HostClients.SeededStudentLogin,
            password = "definitely-wrong-password",
        });

        // then: HTTP 401, message «Неверный логин или пароль» дословно.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Неверный логин или пароль");
    }

    [Fact]
    public async Task InvalidData_RegisterWithEmptyFullName()
    {
        // given: свежий экземпляр.
        using var client = HostClients.Create(_factory);

        // when: регистрация с пустым fullName.
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "",
            login = "ts154-invalid",
            email = "ts154-invalid@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 400, message «Данные заполнены неверно» дословно.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
    }

    [Fact]
    public async Task DuplicateLogin_RegisterWithSeededLogin()
    {
        // given: пользователь с login student01 существует (демо-сид).
        using var client = HostClients.Create(_factory);

        // when: регистрация с занятым логином (свободный email).
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Словарь Дубликат Логина",
            login = "student01",
            email = "ts154-dup-login@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 409, message «Пользователь с таким логином уже существует» дословно.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Пользователь с таким логином уже существует");
    }

    [Fact]
    public async Task DuplicateEmail_RegisterWithSeededEmail()
    {
        // given: email student01@example.com занят (демо-сид).
        using var client = HostClients.Create(_factory);

        // when: регистрация со свободным логином и занятым email.
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Словарь Дубликат Email",
            login = "ts154-dup-email",
            email = "student01@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 409, message «Пользователь с таким email уже существует» дословно.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Пользователь с таким email уже существует");
    }

    [Fact]
    public async Task WrongCurrentPassword_ChangePasswordWithWrongCurrent()
    {
        // given: teacher авторизован.
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        // when: смена пароля с неверным текущим паролем.
        using var response = await client.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "totally-wrong-current",
            password = "Newpass1!",
            confirmPassword = "Newpass1!",
        });

        // then: HTTP 400, message «Неверный текущий пароль» дословно.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Неверный текущий пароль");
    }

    [Fact]
    public async Task RecoveryCodeRejected_ConfirmWithoutLiveCode()
    {
        // given: для email не запрашивался код восстановления (живого кода нет).
        using var client = HostClients.Create(_factory);
        using var request = await client.PostAsJsonAsync("/api/v1/auth/recovery/request", new
        {
            email = "ts154-no-code@example.com",
        });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);

        // when: подтверждение восстановления с кодом, который не был выдан.
        using var response = await client.PostAsJsonAsync("/api/v1/auth/recovery/confirm", new
        {
            email = "ts154-no-code@example.com",
            code = "000000",
        });

        // then: HTTP 400, message «Код восстановления не подходит» дословно
        // (FR-010: неверный код и незарегистрированный email — единый 400).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Код восстановления не подходит");
    }

    [Fact]
    public async Task ResetTokenInvalid_ResetPasswordWithGarbageToken()
    {
        // given: свежий экземпляр (reset-токен не выдавался).
        using var client = HostClients.Create(_factory);

        // when: сброс пароля с заведомо невалидным resetToken.
        using var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            resetToken = "definitely-not-a-valid-reset-token",
            password = "Newpass1!",
            confirmPassword = "Newpass1!",
        });

        // then: HTTP 400, message «Ссылка восстановления недействительна или истекла» дословно.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Ссылка восстановления недействительна или истекла");
    }

    [Fact]
    public async Task DuplicateLab_PostExistingPair()
    {
        // given: teacher авторизован; работа (1,1) существует (демо-сид).
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        // when: POST /api/v1/labs с дублирующей парой.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 1,
            semester = 1,
            content = "Словарь: дубликат пары",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 409, message «Лабораторная с таким номером уже есть в семестре» дословно.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная с таким номером уже есть в семестре");
    }

    [Fact]
    public async Task LabNotFound_PutUnknownLabId()
    {
        // given: teacher авторизован.
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        // when: PUT /api/v1/labs/{нуль-uuid} с валидными полями.
        using var response = await client.PutAsJsonAsync($"/api/v1/labs/{ZeroId}", new
        {
            number = 1,
            semester = 5,
            content = "Словарь: нет работы",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 404, message «Лабораторная не найдена» дословно.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }

    [Fact]
    public async Task DuplicateGroup_PostExistingName()
    {
        // given: teacher авторизован; группа ИК-221 существует (демо-сид).
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        // when: POST /api/v1/groups с занятым названием.
        using var response = await client.PostAsJsonAsync("/api/v1/groups", new { name = "ИК-221" });

        // then: HTTP 409, message «Группа с таким названием уже существует» дословно.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Группа с таким названием уже существует");
    }

    [Fact]
    public async Task GroupNotFound_GetStudentsOfUnknownGroup()
    {
        // given: teacher авторизован.
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        // when: GET /api/v1/groups/{нуль-uuid}/students.
        using var response = await client.GetAsync($"/api/v1/groups/{ZeroId}/students");

        // then: HTTP 404, message «Группа не найдена» дословно.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Группа не найдена");
    }

    [Fact]
    public async Task StudentNotFound_PutSubmissionWithUnknownStudent()
    {
        // given: teacher авторизован; id существующей работы (1,1) из демо-сида.
        using var client = await HostClients.CreateTeacherClientAsync(_factory);
        var labId = await FindLabIdAsync(client, semester: 1, number: 1);

        // when: PUT /api/v1/submissions с несуществующим studentId.
        using var response = await client.PutAsJsonAsync("/api/v1/submissions", new
        {
            studentId = ZeroId,
            labId,
            submitDate = "2026-10-05",
            defenseDate = (string?)null,
        });

        // then: HTTP 404, message «Студент не найден» дословно (порядок FR-022:
        // 404 студент раньше 404 работа).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }

    [Fact]
    public async Task Unauthorized_ProtectedEndpointWithoutCookie()
    {
        // given: cookies отсутствуют.
        using var client = HostClients.Create(_factory);

        // when: запрос защищённого эндпойнта без авторизации.
        using var response = await client.GetAsync("/api/v1/labs");

        // then: HTTP 401, message «Не авторизован» дословно.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Не авторизован");
    }

    [Fact]
    public async Task Forbidden_StudentOnTeacherEndpoint()
    {
        // given: вход student01 (демо-сид, роль student).
        using var client = await HostClients.CreateSeededStudentClientAsync(_factory);

        // when: студент обращается к teacher-only эндпойнту.
        using var response = await client.GetAsync("/api/v1/labs");

        // then: HTTP 403, message «Доступ запрещён» дословно.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Доступ запрещён");
    }

    [Fact]
    public async Task NotFound_UnknownApiRoute()
    {
        // given: свежий экземпляр.
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/nonexistent.
        using var response = await client.GetAsync("/api/v1/nonexistent");

        // then: HTTP 404, message «Не найдено» дословно (технический 404 вне §8, NFR-007).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Не найдено");
    }

    /// <summary>id работы по паре (semester, number) из списка лабораторных (FR-013).</summary>
    private static async Task<string> FindLabIdAsync(HttpClient client, int semester, int number)
    {
        using var response = await client.GetAsync($"/api/v1/labs?semester={semester}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        var match = root.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("number").GetInt32() == number);
        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"Предусловие кейса: работа ({semester},{number}) не найдена в демо-сиде.");
        return match.GetProperty("id").GetString()!;
    }
}
