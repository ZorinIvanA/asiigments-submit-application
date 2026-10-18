using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-194 «PUT /me/profile: границы длины email 254/255» (boundary, FR-021, P1).
///
/// given: сессия пользователя; выбранные адреса не заняты другими пользователями
///        (свежее хранилище фикстуры класса); fullName валиден.
/// when:  PUT /api/v1/me/profile с email ровно из 254 символов валидного формата
///        ('a'×249 + '@d.zz'); отдельно ровно из 255 ('a'×250 + '@d.zz').
/// then:  254 → 200 (email обновлён); 255 → 400 «Данные заполнены неверно»,
///        errors.email = ['Email — не более 254 символов'] (словарь ошибок
///        валидации, дословно). Ограничение User.email: «1–254 симв. после трима»
///        (граница 254 включительно).
/// </summary>
public sealed class Ts194_ProfileEmailLengthBoundaryTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "ts194";
    private const string Email = "ts194@x.ru";
    private const string Domain = "@d.zz";

    private readonly B07WebAppFactory _factory;

    public Ts194_ProfileEmailLengthBoundaryTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Email_Exactly254Accepted_Exactly255Rejected()
    {
        // given: сессия пользователя; адреса 254/255 в свежем хранилище свободны.
        var (client, _) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: "Имя", login: Login, email: Email);

        // when: email ровно 254 символа валидного формата.
        var email254 = new string('a', 254 - Domain.Length) + Domain;
        Assert.Equal(254, email254.Length);
        using var accepted = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = email254,
        });

        // then: 200 — граница 254 включительно, email обновлён (ответ и хранилище).
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(accepted);
        BodyAssertions.StringPropertyIs(root, "email", email254);
        var stored = HostClients.ResolveUserByEmail(_factory, email254);
        Assert.Equal(email254, stored.Email);

        // when: email ровно 255 символов.
        var email255 = new string('a', 255 - Domain.Length) + Domain;
        Assert.Equal(255, email255.Length);
        using var rejected = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = email255,
        });

        // then: 400 «Данные заполнены неверно», errors.email дословно из словаря.
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var rejectedRoot = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.MessageIs(rejectedRoot, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(rejectedRoot, "email", "Email — не более 254 символов");
    }
}
