using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-082 «me/profile PUT: пробельное ФИО и собственный email» (boundary,
/// FR-015, P1) — два самостоятельных под-сценария кейса, каждый в СВОЕМ
/// тестовом классе с собственной фикстурой: given «текущий email» выполняется
/// буквально в обоих — у каждого пользователя хранилища свой адрес (ci-
/// уникальность email IUserRepository); текущий пользователь приложения —
/// засеянный преподаватель (Seed__TeacherLogin, email 'teacher@example.com'),
/// клиент анонимный (аутентификации в приложении нет).
///
/// Под-сценарий 1 (Ts082_MeProfileBlankFullNameTests):
///   given: текущий пользователь — засеянный преподаватель (email teacher@example.com);
///   when:  PUT {fullName:'  ', email:'a@b.ru'};
///   then:  400, errors.fullName=['Заполните поле']
///          (FR-015 AC «Пустое ФИО»).
///
/// Под-сценарий 2 (Ts082_MeProfileOwnEmailNoConflictTests):
///   given: текущий пользователь — засеянный преподаватель (email teacher@example.com);
///   when:  PUT {fullName:'Ф И О', email:'teacher@example.com'} (без изменения);
///   then:  200 — совпадение email с самим собой — НЕ 409
///          (FR-015 AC «Свой email не конфликтует»).
/// </summary>
public sealed class Ts082_MeProfileBlankFullNameTests : IClassFixture<B15WebAppFactory>
{
    private const string ValidEmail = "a@b.ru";

    private readonly B15WebAppFactory _factory;

    public Ts082_MeProfileBlankFullNameTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WhitespaceFullName_ReturnsRequiredError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        using var client = B15Harness.CreateClient(_factory);

        // when: PUT с пробельным ФИО и валидным email.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "  ",
            email = ValidEmail,
        });

        // then: 400, errors.fullName = ['Заполните поле'] — ровно текст required.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(envelope, "fullName", "Заполните поле");
    }
}

public sealed class Ts082_MeProfileOwnEmailNoConflictTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "teacher";
    private const string CurrentEmail = "teacher@example.com";

    private readonly B15WebAppFactory _factory;

    public Ts082_MeProfileOwnEmailNoConflictTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithOwnUnchangedEmail_ReturnsOk()
    {
        // given: текущий пользователь — засеянный преподаватель с email
        // teacher@example.com; клиент анонимный.
        var current = await B15Harness.CurrentUserAsync(_factory);
        Assert.Equal(CurrentEmail, current.Email);
        using var client = B15Harness.CreateClient(_factory);

        // when: PUT без изменения email (совпадает с собственным).
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Ф И О",
            email = CurrentEmail,
        });

        // then: 200 — совпадение email с самим собой не является конфликтом;
        // ProfileDto отражает обновлённые значения.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(body, "login", Login);
        BodyAssertions.StringPropertyIs(body, "email", CurrentEmail);
        BodyAssertions.StringPropertyIs(body, "fullName", "Ф И О");
        BodyAssertions.StringPropertyIs(body, "role", "teacher");
    }
}
