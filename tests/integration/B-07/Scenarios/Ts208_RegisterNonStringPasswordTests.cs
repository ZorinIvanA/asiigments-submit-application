using System.Text;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-208 «Регистрация: нестроковые password/repeatPassword — как пустые строки»
/// (negative, FR-006, P2).
///
/// given: логин 'nsuser' и email 'ns@example.com' свободны; счётчик KDF обнулён
///        (зонд Δkdf подключён до запроса — база нулевая).
/// when:  POST /auth/register {fullName:'И И И', login:'nsuser',
///        email:'ns@example.com', password:12345 (JSON-число),
///        repeatPassword:'Passw0rd!'}.
/// then:  400 (не 500); message 'Данные заполнены неверно'; errors.password —
///        ровно 4 текста (нестроковый password трактуется как пустая строка, для
///        которой срабатывают все правила сразу: min/digit/letter/special);
///        errors.repeatPassword=['Пароли не совпадают'] ('' ≠ 'Passw0rd!');
///        пользователь не создан; Δkdf=0 (валидация до любых дериваций).
///        Паритет с TS-035: нестроковые поля — пустые строки (FR-006).
/// </summary>
public sealed class Ts208_RegisterNonStringPasswordTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts208_RegisterNonStringPasswordTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NumericPasswordTreatedAsEmptyString_BadRequestWithAllRulesAndZeroKdf()
    {
        // given: 'nsuser'/'ns@example.com' свободны; счётчик KDF обнулён —
        // зонд Δkdf подключён до запроса (база зонда нулевая).
        using var client = B07AuthClients.CreateClient(_factory);
        using var kdf = B07KdfProbe.Attach(_factory);

        // when: POST /auth/register с password как JSON-числом 12345
        // (raw-JSON тело: PostAsJsonAsync типизированного объекта число не даст).
        const string body =
            "{\"fullName\":\"И И И\",\"login\":\"nsuser\",\"email\":\"ns@example.com\","
            + "\"password\":12345,\"repeatPassword\":\"Passw0rd!\"}";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(B07AuthClients.RegisterEndpoint, content);

        // then: 400 (не 500) с конвертом валидации.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");

        // then: errors.password — ровно 4 текста: пустая строка нарушает все
        // правила пароля сразу (min/digit/letter/special), порядок — словарь.
        BodyAssertions.ErrorFieldEquals(
            root,
            "password",
            "Пароль должен содержать не менее 8 символов",
            "Пароль должен содержать хотя бы одну цифру",
            "Пароль должен содержать хотя бы одну букву",
            "Пароль должен содержать хотя бы один специальный знак");

        // then: errors.repeatPassword — ['Пароли не совпадают'] ('' ≠ 'Passw0rd!').
        BodyAssertions.ErrorFieldEquals(root, "repeatPassword", "Пароли не совпадают");

        // then: пользователь не создан — ни по логину, ни по email (ci-поиск).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(users.GetByLogin("nsuser"));
        Assert.Null(users.GetByEmail("ns@example.com"));

        // then: Δkdf=0 — валидация отвечает отказом до любых операций KDF.
        Assert.Equal(0, kdf.OperationsCount);
    }
}
