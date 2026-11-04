using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-194 «PUT /me/profile: границы длины email 254/255» (boundary, FR-021, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher»); клиент анонимный (аутентификации в
///        приложении нет); выбранные адреса не заняты другими пользователями
///        (свежее хранилище фикстуры класса); fullName валиден.
/// when:  PUT /api/v1/me/profile с email ровно из 254 символов валидного формата
///        ('a'×249 + '@d.zz'); отдельно ровно из 255 ('a'×250 + '@d.zz').
/// then:  254 → 200 (email обновлён); 255 → 400 «Данные заполнены неверно»,
///        errors.email=['Email — не более 254 символов'].
///        Ограничение User.email: «1–254 симв. после трима»; «Словарь ошибок
///        валидации»: «Email — не более 254 символов».
/// </summary>
public sealed class Ts194_ProfileEmailLengthBoundaryTests : IClassFixture<B15WebAppFactory>
{
    private readonly B15WebAppFactory _factory;

    public Ts194_ProfileEmailLengthBoundaryTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Email_Exactly254CharsValidFormat_ReturnsOkAndUpdates()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный;
        // выбранный целевой адрес 254 символа свободен (свежее хранилище фикстуры класса).
        var current = await B15Harness.CurrentUserAsync(_factory);
        using var client = B15Harness.CreateClient(_factory);

        // when: email ровно 254 символа валидного формата ('a'×249 + '@d.zz').
        var email254 = new string('a', 249) + "@d.zz";
        Assert.Equal(254, email254.Length);
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = email254,
        });

        // then: граница включительно — 200, email в хранилище обновлён.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await B15Harness.UserByIdAsync(_factory, current.Id);
        Assert.Equal(email254, stored.Email);
    }

    [Fact]
    public async Task Email_Exactly255CharsValidFormat_Returns400WithDictionaryText()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный;
        // выбранный целевой адрес 255 символов свободен (свежее хранилище фикстуры класса).
        await B15Harness.CurrentUserAsync(_factory);
        using var client = B15Harness.CreateClient(_factory);

        // when: email ровно 255 символов валидного формата ('a'×250 + '@d.zz').
        var email255 = new string('a', 250) + "@d.zz";
        Assert.Equal(255, email255.Length);
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = email255,
        });

        // then: 400 с дословным message и словарной ошибкой поля email.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(envelope, "email", "Email — не более 254 символов");
    }
}
