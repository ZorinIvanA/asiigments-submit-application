using System.Text;
using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-092 «Границы ФИО: 200/201 после трима» (boundary, FR-021, P1).
///
/// given: сессия пользователя (собственная учётка регистрации).
/// when:  PUT /me/profile с fullName ровно 200 символов; отдельно ровно 201.
/// then:  200 → HTTP 200; 201 → 400 «Данные заполнены неверно»,
///        errors.fullName = ['ФИО — от 1 до 200 символов'] (словарь ошибок
///        валидации, дословно). FR-021 AC «Невалидное ФИО».
/// </summary>
public sealed class Ts092_FullNameBoundaryTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "ts092";
    private const string Email = "ts092@x.ru";

    private readonly B07WebAppFactory _factory;

    public Ts092_FullNameBoundaryTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FullName_Exactly200Accepted_Exactly201Rejected()
    {
        // given: сессия пользователя.
        var (client, _) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: "Текущее Имя", login: Login, email: Email);

        // when: fullName ровно 200 символов после трима.
        var fullName200 = new string('Ф', 200);
        using var accepted = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = fullName200,
            email = Email,
        });

        // then: HTTP 200 — граница включительно.
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // when: fullName ровно 201 символ.
        var fullName201 = new StringBuilder(201).Insert(0, "Ф", 201).ToString();
        Assert.Equal(201, fullName201.Length);
        using var rejected = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = fullName201,
            email = Email,
        });

        // then: 400 «Данные заполнены неверно», errors.fullName дословно из словаря.
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(rejected);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(root, "fullName", "ФИО — от 1 до 200 символов");
    }
}
