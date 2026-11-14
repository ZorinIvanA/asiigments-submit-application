using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-040 «Пароль 128/129 символов: верхняя граница до хэширования» (boundary, P0,
/// FR-012 AC «Верхняя граница пароля»; ASM-015: граница закрывает CPU-амплитикацию
/// PBKDF2 — проверяется до хэширования).
///
/// given: RemoteIpAddress=10.0.0.40; с этого IP выполнено 0 попыток регистрации
///        (лимит 5/час на IP не исчерпан — обе попытки сценария укладываются в лимит).
/// when:  регистрация с password из 128 символов валидной структуры
///        (буква+цифра+спецзнак); отдельно из 129.
/// then:  128 → 201; 129 → 400 «Данные заполнены неверно», errors.password содержит
///        «Пароль — не более 128 символов».
/// </summary>
public sealed class Ts040_PasswordMaxBoundaryTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts040_PasswordMaxBoundaryTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithPasswordOf128And129Characters_Accepts128Rejects129()
    {
        // given: RemoteIpAddress=10.0.0.40, попыток с этого IP ещё не было.
        using var client = HostClients.Create(_factory);

        // Пароли валидной структуры: буквы + цифра + спецзнак; длина 128 и 129.
        var passwordOf128 = new string('a', 126) + "1!";
        var passwordOf129 = new string('a', 127) + "1!";

        // when: регистрация с паролем из 128 символов.
        using var accepted = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.40",
            fullName: "Граница Сто Двадцать Восемь",
            login: "ts040.p128",
            email: "ts040.p128@example.com",
            password: passwordOf128,
            repeatPassword: passwordOf128);

        // then: 128 → 201 (граница включительно, верхняя граница ДО хэширования не режет).
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        // when: регистрация с паролем из 129 символов (второй логин/email — свободные).
        using var rejected = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.40",
            fullName: "Граница Сто Двадцать Девять",
            login: "ts040.p129",
            email: "ts040.p129@example.com",
            password: passwordOf129,
            repeatPassword: passwordOf129);

        // then: 129 → 400 «Данные заполнены неверно», errors.password содержит
        // «Пароль — не более 128 символов».
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.HasExactlyProperties(root, "message", "errors");
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldContains(root, "password", "Пароль — не более 128 символов");
    }
}
