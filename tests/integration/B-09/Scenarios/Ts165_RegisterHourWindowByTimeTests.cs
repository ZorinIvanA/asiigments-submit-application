using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-165 «Матрица register: часовое окно 3600с доказано временем (скольжение)»
/// (boundary, FR-004 + FR-006, P1).
///
/// given: хост Development с инжектируемыми часами (FakeTimeProvider.SetUtcNow)
///        и фиксированным IP тестового клиента; часы на T0; с этого IP выполнены
///        5 POST /auth/register с РАЗНЫМИ свободными логинами reg01..reg05
///        (все — 201); логины reg06/reg07 и email
///        reg06@example.com/reg07@example.com свободны.
/// when:  в том же окне (часы T0): 6-й POST /auth/register с валидным телом
///        {fullName:'Рега Шесть', login:'reg06', email:'reg06@example.com',
///        password:'Passw0rd!', repeatPassword:'Passw0rd!'} — контрольная
///        точка; затем часы переведены на T0+3600001 мс (строго за границу
///        заявленного окна 3600 с) и выполнен POST /auth/register
///        {fullName:'Рега Семь', login:'reg07', email:'reg07@example.com',
///        password:'Passw0rd!', repeatPassword:'Passw0rd!'}.
/// then:  запрос в окне — 429 RATE_LIMITED 'Слишком много попыток. Повторите
///        позже'; запрос после перевода часов — НЕ 429: 201 с созданием
///        пользователя reg07 (метки T0 вычищены — окно 3600 с доказано
///        временем; при окне 600 с или 86400 с исход этой пары запросов
///        отличался бы) (FR-004 матрица register: 5/3600с по IP).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-07 («ЕДИНСТВЕННАЯ зона исполнения кейса»; в зоне
/// tests/integration/B-08 идентификатор Ts165 занят поддеревом Groups). В зоне
/// tests/integration/B-09 идентификатор Ts165 свободен. Поведенческая часть
/// кейса исполнима дословно и исполнена в собственной зоне батча B-09
/// (прецедент c-1052); расхождение размещения зафиксировано в
/// scenario_change_requests.
/// </summary>
public sealed class Ts165_RegisterHourWindowByTimeTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private static readonly DateTimeOffset T0 = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Password = "Passw0rd!";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task SixthRegisterInWindow429_SeventhAfter3600001Ms_201()
    {
        // given: часы на T0; 5 регистраций с одного IP разными свободными
        // логинами (все — 201); все запросы клиента идут с одним RemoteIpAddress
        // (TestServer) — один ключ 'IP' регистрационного лимитера.
        _factory.Time.SetUtcNow(T0);
        var client = B09HostClients.Create(_factory);
        for (var index = 1; index <= 5; index++)
        {
            using var response = await PostRegister(
                client, $"Рега {NumberWord(index)}", $"reg0{index}", $"reg0{index}@example.com");
            B09AuthSupport.AssertStatus(
                response, HttpStatusCode.Created, $"предусловие: регистрация reg0{index} (TS-165)");
        }

        // when: 6-й POST /auth/register в том же окне (часы T0) — валидное тело.
        using var sixth = await PostRegister(
            client, "Рега Шесть", "reg06", "reg06@example.com");

        // then: 429 RATE_LIMITED 'Слишком много попыток. Повторите позже'
        // (TryAcquire ДО разбора и валидации тела — FR-004).
        var sixthBody = await B09Assertions.ParseObjectAsync(sixth, HttpStatusCode.TooManyRequests, "6-я регистрация в окне (TS-165)");
        B09Assertions.MessageIs(sixthBody, B09AuthSupport.RateLimitedMessage);

        // when: часы переведены на T0+3600001 мс (строго за границу окна 3600 с);
        // 7-й POST /auth/register.
        _factory.Time.SetUtcNow(T0.AddMilliseconds(3_600_001));
        using var seventh = await PostRegister(
            client, "Рега Семь", "reg07", "reg07@example.com");

        // then: НЕ 429 — 201 с созданием пользователя reg07 (метки T0 вычищены —
        // окно 3600 с доказано временем; при окне 600 с или 86400 с исход
        // отличался бы).
        _ = await B09Assertions.ParseObjectAsync(seventh, HttpStatusCode.Created, "7-я регистрация после T0+3600001 мс (TS-165)");
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin("reg07"));
    }

    /// <summary>POST /auth/register с телом кейса (значения экранирования не требуют).</summary>
    private static Task<HttpResponseMessage> PostRegister(HttpClient client, string fullName, string login, string email) =>
        client.PostAsJsonAsync(B09HostClients.RegisterEndpoint, new
        {
            fullName,
            login,
            email,
            password = Password,
            repeatPassword = Password,
        });

    private static string NumberWord(int index) =>
        index switch
        {
            1 => "Один",
            2 => "Два",
            3 => "Три",
            4 => "Четыре",
            5 => "Пять",
            _ => index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}
