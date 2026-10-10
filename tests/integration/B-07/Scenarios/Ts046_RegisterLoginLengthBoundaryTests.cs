using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-046 «Границы длины логина 100/101» (boundary, FR-012, P1).
///
/// given: RemoteIpAddress=10.0.0.46; с этого IP выполнено 0 попыток регистрации
///        (лимит 5/час на IP не исчерпан — обе попытки сценария укладываются в лимит).
/// when:  регистрация с login из 100 символов [A-Za-z0-9._-]; отдельно из 101.
/// then:  100 → 201; 101 → 400, errors.login = ['Логин — от 1 до 100 символов']
///        (словарь ошибок валидации, FR-012).
/// </summary>
public sealed class Ts046_RegisterLoginLengthBoundaryTests : IClassFixture<B07AuthWebAppFactory>
{
    private const string ClientIp = "10.0.0.46";

    private readonly B07AuthWebAppFactory _factory;

    public Ts046_RegisterLoginLengthBoundaryTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login100Accepted_Login101RejectedWithDictionaryText()
    {
        // given: RemoteIpAddress=10.0.0.46; попыток регистрации с этого IP нет
        // (свежая фикстура класса — лимитеры и хранилище пусты).
        using var client = B07AuthClients.CreateClientWithIp(_factory, ClientIp);

        // when: регистрация с login из 100 символов [A-Za-z0-9._-].
        using var accepted = await client.PostAsJsonAsync(B07AuthClients.RegisterEndpoint, new
        {
            fullName = "Граница Сто",
            login = BuildAlphabetLogin(100),
            email = "ts046-100@x.ru",
            password = HostClients.TestUserPassword,
            repeatPassword = HostClients.TestUserPassword,
        });

        // then: 100 → 201.
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        // when: отдельно регистрация с login из 101 символа.
        using var rejected = await client.PostAsJsonAsync(B07AuthClients.RegisterEndpoint, new
        {
            fullName = "Граница Сто Один",
            login = BuildAlphabetLogin(101),
            email = "ts046-101@x.ru",
            password = HostClients.TestUserPassword,
            repeatPassword = HostClients.TestUserPassword,
        });

        // then: 101 → 400, errors.login = ['Логин — от 1 до 100 символов'] дословно.
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(root, "login", "Логин — от 1 до 100 символов");
    }

    /// <summary>Логин заданной длины из допустимого алфавита [A-Za-z0-9._-].</summary>
    private static string BuildAlphabetLogin(int length)
    {
        const string alphabet = "ab1._-";
        var chars = new char[length];
        for (var index = 0; index < length; index++)
        {
            chars[index] = alphabet[index % alphabet.Length];
        }

        return new string(chars);
    }
}
