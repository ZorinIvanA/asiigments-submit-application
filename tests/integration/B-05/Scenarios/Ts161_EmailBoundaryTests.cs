using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-161 «Граница длины email при регистрации: 254 включительно, 255 — 400»
/// (boundary, FR-007).
///
/// given: свежий экземпляр приложения (собственная фикстура класса — будет
///        ровно 2 попытки); логины свободны; email длиной ровно 254 символа
///        сконструирован валидно по формату (локальная часть + доменные метки
///        через точки, без пробелов, одна @).
/// when:  POST /api/v1/auth/register с email ровно 254 символа; затем с email
///        255 символов (тот же адрес, удлинённый на один символ в локальной
///        части).
/// then:  первый — HTTP 201; второй — HTTP 400 «Данные заполнены неверно» с
///        непустым errors.email. FR-007 / модель User.email: «длина 1–254
///        после трима» (граница 254 включительно).
/// </summary>
public sealed class Ts161_EmailBoundaryTests : IClassFixture<B05WebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";
    private const string Domain = "@mail.example.com";

    private readonly B05WebAppFactory _factory;

    public Ts161_EmailBoundaryTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Email_Exactly254Accepted_Exactly255Rejected()
    {
        // given: валидный по формату адрес длиной ровно 254 символа
        // (локальная часть + доменные метки через точки, без пробелов, одна @).
        var email254 = new string('x', 254 - Domain.Length) + Domain;
        Assert.Equal(254, email254.Length);
        Assert.Equal(1, email254.Count(character => character == '@'));
        Assert.DoesNotContain(' ', email254);

        using var client = HostClients.Create(_factory);

        // when: регистрация с email ровно 254 символа.
        using var accepted = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Граница Email Двести Пятьдесят Четыре",
            login = "ts161email254",
            email = email254,
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 201 — граница 254 включительно.
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        // when: тот же адрес, удлинённый на один символ в локальной части (255).
        var email255 = new string('x', 255 - Domain.Length) + Domain;
        Assert.Equal(255, email255.Length);
        using var rejected = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Граница Email Двести Пятьдесят Пять",
            login = "ts161email255",
            email = email255,
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 400 «Данные заполнены неверно» с непустым errors.email.
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsNonEmptyStringArray(root, "email");
    }
}
