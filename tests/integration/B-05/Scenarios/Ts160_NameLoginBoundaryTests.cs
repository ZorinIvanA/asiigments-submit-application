using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-160 «Границы длин fullName и login при регистрации: 200/201 и 100/101»
/// (boundary, FR-007).
///
/// given: свежий экземпляр приложения (собственная фикстура класса; суммарно
///        ровно 4 попытки — лимит 5/час не срабатывает); свободные login/email
///        для каждой попытки.
/// when:  POST /api/v1/auth/register с fullName ровно 200 символов (прочие поля
///        валидны); затем fullName ровно 201 символ; затем login ровно 100
///        символов из [A-Za-z0-9._-]; затем login ровно 101 символ.
/// then:  fullName 200 и login 100 — HTTP 201 (границы включительно);
///        fullName 201 — HTTP 400 «Данные заполнены неверно» с непустым
///        errors.fullName; login 101 — HTTP 400 с непустым errors.login.
///        FR-007: «fullName 1–200 после трима; login 1–100 после трима, только
///        [A-Za-z0-9._-]».
/// </summary>
public sealed class Ts160_NameLoginBoundaryTests : IClassFixture<B05WebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private readonly B05WebAppFactory _factory;

    public Ts160_NameLoginBoundaryTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FullName_Exactly200Accepted_Exactly201Rejected()
    {
        // given: свободные login/email (2 попытки из бюджета 4).
        using var client = HostClients.Create(_factory);

        // when: fullName ровно 200 символов.
        using var accepted = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = new string('Б', 200),
            login = "ts160name200",
            email = "ts160name200@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 201 — граница 200 включительно.
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        // when: fullName ровно 201 символ.
        using var rejected = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = new string('В', 201),
            login = "ts160name201",
            email = "ts160name201@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 400 «Данные заполнены неверно» с непустым errors.fullName.
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsNonEmptyStringArray(root, "fullName");
    }

    [Fact]
    public async Task Login_Exactly100Accepted_Exactly101Rejected()
    {
        // given: свободные login/email (ещё 2 попытки из бюджета 4; суммарно 4 &lt; 5/час).
        using var client = HostClients.Create(_factory);

        // when: login ровно 100 символов из [A-Za-z0-9._-].
        var login100 = new string('a', 99) + "9";
        Assert.Equal(100, login100.Length);
        using var accepted = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Граница Логина Сто",
            login = login100,
            email = "ts160login100@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 201 — граница 100 включительно.
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        // when: login ровно 101 символ.
        var login101 = new string('a', 100) + "9";
        Assert.Equal(101, login101.Length);
        using var rejected = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Граница Логина Сто Один",
            login = login101,
            email = "ts160login101@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 400 «Данные заполнены неверно» с непустым errors.login.
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsNonEmptyStringArray(root, "login");
    }
}
