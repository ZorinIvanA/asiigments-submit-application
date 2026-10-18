using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-170 «me/profile PUT: некорректный формат email — 400 errors.email»
/// (negative, FR-015 + FR-023, P1; ревью R4b).
///
/// given: пользователь авторизован; fullName валиден; все тестируемые email
///        свободны.
/// when:  PUT /api/v1/me/profile {fullName:'Ф И О', email:'abc'}; отдельно
///        {email:'a@b'} (домен без точки/зоны); отдельно {email:'a b@c.ru'}
///        (пробел внутри).
/// then:  все три — 400 'Данные заполнены неверно',
///        errors.email=['Введите корректный email'] — формат User.email
///        «логин@домен.зона (без пробелов, '@' одна, домен из непустых меток
///        через точку, зона непустая)» валидируется на PUT профиля по правилам
///        FR-006, текст — из словаря ERROR_TEXTS.email (FR-015: «валидация как
///        во FR-006 (errors {fullName, email})»); профиль не изменён.
/// </summary>
public sealed class Ts170_ProfilePutInvalidEmailFormatTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts170";
    private const string Email = "ts170@x.ru";
    private const string ValidFullName = "Ф И О";

    /// <summary>Три значения when кейса: без '@', домен без точки/зоны, пробел внутри.</summary>
    public static readonly string[] InvalidEmails =
    {
        "abc",
        "a@b",
        "a b@c.ru",
    };

    private readonly B17WebAppFactory _factory;

    public Ts170_ProfilePutInvalidEmailFormatTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithEachInvalidEmailFormat_Returns400WithEmailError_AndKeepsProfile()
    {
        // given: пользователь авторизован; fullName валиден.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Прежнее ФИО",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        foreach (var invalidEmail in InvalidEmails)
        {
            // when: PUT с валидным ФИО и некорректным форматом email (отдельный
            // запрос на каждое значение when кейса).
            using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
            {
                fullName = ValidFullName,
                email = invalidEmail,
            });

            // then: 400 «Данные заполнены неверно»;
            // errors.email = ['Введите корректный email'] дословно.
            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest,
                $"email «{invalidEmail}»: ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var root = await B17BodyAssertions.ReadRootObjectAsync(response);
            B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
            B17BodyAssertions.ErrorFieldEquals(root, "email", ErrorTexts.EmailFormat);

            // then: профиль не изменён — хранимые fullName/email прежние.
            var stored = B17ProfileHost.StoredUser(_factory, user.Id);
            Assert.True(
                string.Equals(stored.Email, Email, StringComparison.Ordinal),
                $"email «{invalidEmail}»: ожидался неизменный хранимый email «{Email}», фактически «{stored.Email}».");
            Assert.True(
                string.Equals(stored.FullName, "Прежнее ФИО", StringComparison.Ordinal),
                $"email «{invalidEmail}»: ожидалось неизменное хранимое ФИО, фактически «{stored.FullName}».");
        }
    }
}
