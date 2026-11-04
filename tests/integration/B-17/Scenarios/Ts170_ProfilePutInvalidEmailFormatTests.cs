using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-170 «me/profile PUT: некорректный формат email — 400 errors.email»
/// (negative, FR-015 + FR-023, P1; ревью R4b).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); клиент
///        анонимный (аутентификации в приложении нет); fullName валиден.
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
    private const string StoredEmail = "teacher@example.com";
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
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        var current = await B17ProfileHost.CurrentUserAsync(_factory);
        Assert.Equal(StoredEmail, current.Email);
        using var client = B17ProfileHost.Create(_factory);

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
            var stored = await B17ProfileHost.StoredUserAsync(_factory, current.Id);
            Assert.True(
                string.Equals(stored.Email, StoredEmail, StringComparison.Ordinal),
                $"email «{invalidEmail}»: ожидался неизменный хранимый email «{StoredEmail}», фактически «{stored.Email}».");
            Assert.True(
                string.Equals(stored.FullName, current.FullName, StringComparison.Ordinal),
                $"email «{invalidEmail}»: ожидалось неизменное хранимое ФИО, фактически «{stored.FullName}».");
        }
    }
}
