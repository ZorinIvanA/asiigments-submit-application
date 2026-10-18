using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-194 «PUT /me/profile: границы длины email 254/255» (boundary, FR-021, P1).
///
/// given: пользователь role=student создан прямым DI-сидом в IUserRepository
///        тестового хоста; сессия — cookie access_token с JWT HS256, минтым
///        харнесом ключом Auth__JwtKey тестового хоста; POST /auth/register и
///        POST /auth/login в предусловиях НЕ вызываются (CR-001, арбитраж a-017);
///        выбранные адреса не заняты другими пользователями; fullName валиден.
/// when:  PUT /api/v1/me/profile с email ровно из 254 символов валидного формата
///        ('a'×249 + '@d.zz'); отдельно ровно из 255 ('a'×250 + '@d.zz').
/// then:  254 → 200 (email обновлён); 255 → 400 «Данные заполнены неверно»,
///        errors.email=['Email — не более 254 символов'].
///        Ограничение User.email: «1–254 симв. после трима»; «Словарь ошибок
///        валидации»: «Email — не более 254 символов».
///        Изоляция методов (реворк-правка CR-001): IClassFixture-хост и его
///        in-memory хранилище ОБЩИЕ для всех [Fact] класса, а IUserRepository.Add
///        бросает StorageConflictException на повторных ci-ключах login/email —
///        идентичности сид-пользователей уникальны на каждый метод.
/// </summary>
public sealed class Ts194_ProfileEmailLengthBoundaryTests : IClassFixture<B15WebAppFactory>
{
    private readonly B15WebAppFactory _factory;

    public Ts194_ProfileEmailLengthBoundaryTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Email_Exactly254CharsValidFormat_ReturnsOkAndUpdates()
    {
        // given: студент (DI-сид, уникальная идентичность метода — CR-001) и сессия;
        // выбранный целевой адрес 254 символа свободен (свежее хранилище фикстуры класса).
        const string login = "ts194-len254";
        const string currentEmail = "ts194-len254@x.ru";
        var seeded = B15Harness.SeedStudent(_factory, fullName: "Текущее Имя", login: login, email: currentEmail);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

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
        var stored = B15Harness.UserById(_factory, seeded.Id);
        Assert.Equal(email254, stored.Email);
    }

    [Fact]
    public async Task Email_Exactly255CharsValidFormat_Returns400WithDictionaryText()
    {
        // given: студент (DI-сид, уникальная идентичность метода — CR-001) и сессия;
        // выбранный целевой адрес 255 символов свободен (свежее хранилище фикстуры класса).
        const string login = "ts194-len255";
        const string currentEmail = "ts194-len255@x.ru";
        var seeded = B15Harness.SeedStudent(_factory, fullName: "Текущее Имя", login: login, email: currentEmail);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

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
