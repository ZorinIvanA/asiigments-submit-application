using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-082 «me/profile PUT: пробельное ФИО и собственный email» (boundary,
/// FR-015, P1) — два самостоятельных под-сценария кейса, каждый в СВОЕМ
/// тестовом классе с собственной фикстурой: given «текущий email
/// 'x@example.com'» выполняется буквально в обоих (ci-уникальность email
/// IUserRepository не даёт двум пользователям одного хранилища этот адрес).
///
/// Под-сценарий 1 (Ts082_MeProfileBlankFullNameTests):
///   given: пользователь авторизован с текущим email 'x@example.com';
///   when:  PUT {fullName:'  ', email:'a@b.ru'};
///   then:  400, errors.fullName=['Заполните поле']
///          (FR-015 AC «Пустое ФИО»).
///
/// Под-сценарий 2 (Ts082_MeProfileOwnEmailNoConflictTests):
///   given: пользователь авторизован с текущим email 'x@example.com';
///   when:  PUT {fullName:'Ф И О', email:'x@example.com'} (без изменения);
///   then:  200 — совпадение email с самим собой — НЕ 409
///          (FR-015 AC «Свой email не конфликтует»).
/// </summary>
public sealed class Ts082_MeProfileBlankFullNameTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "ts082blank";
    private const string CurrentEmail = "x@example.com";
    private const string ValidEmail = "a@b.ru";

    private readonly B15WebAppFactory _factory;

    public Ts082_MeProfileBlankFullNameTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WhitespaceFullName_ReturnsRequiredError()
    {
        // given: пользователь авторизован с текущим email 'x@example.com'.
        var user = B15Harness.SeedStudent(_factory, fullName: "Прежнее ФИО", login: Login, email: CurrentEmail);
        using var client = B15Harness.CreateSessionClient(_factory, user.Id);

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
    private const string Login = "ts082own";
    private const string CurrentEmail = "x@example.com";

    private readonly B15WebAppFactory _factory;

    public Ts082_MeProfileOwnEmailNoConflictTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithOwnUnchangedEmail_ReturnsOk()
    {
        // given: пользователь авторизован с текущим email 'x@example.com'.
        var user = B15Harness.SeedStudent(_factory, fullName: "Прежнее ФИО", login: Login, email: CurrentEmail);
        using var client = B15Harness.CreateSessionClient(_factory, user.Id);

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
        BodyAssertions.StringPropertyIs(body, "role", "student");
    }
}
