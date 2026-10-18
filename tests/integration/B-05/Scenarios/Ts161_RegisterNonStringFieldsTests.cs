using System.Text;
using LabsApp.Auth;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-161 «Регистрация: не-строковые значения полей обрабатываются как пустые
/// строки» (negative, FR-006/FR-023).
///
/// given: регистрационный лимит не исчерпан (свежая фикстура класса — пустые
///        счётчики); счётчик KDF снимком до запроса; пользователь с такими
///        login/email не создаётся в этом прогоне (login/email во входе —
///        не строковые значения).
/// when:  POST /api/v1/auth/register {fullName:123 (JSON-число), login:null,
///        email:true (JSON-булево), password:'Passw0rd!',
///        repeatPassword:'Passw0rd!'}
/// then:  400 'Данные заполнены неверно' (не 500 и не ошибка десериализации);
///        errors.fullName=['Заполните поле'], errors.login=['Заполните поле'],
///        errors.email=['Заполните поле'] — не-строковые значения трактуются
///        как пустые строки с правилами required; ошибок password/
///        repeatPassword нет; пользователь не создан; Δkdf=0.
/// </summary>
public sealed class Ts161_RegisterNonStringFieldsTests : IClassFixture<B05WebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private readonly B05WebAppFactory _factory;

    public Ts161_RegisterNonStringFieldsTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NonStringFieldValues_AreTreatedAsEmptyStrings_WithoutKdfAndUserCreation()
    {
        // given: снимки числа студентов и суммарного счётчика KDF до запроса.
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var studentsBefore = users.ListStudents().Count;
        var kdfCounter = _factory.Services.GetRequiredService<IKdfCounter>();
        var kdfBefore = TotalDerivations(kdfCounter);

        using var client = HostClients.Create(_factory);

        // when: регистрация с нестроковыми fullName (число), login (null),
        //       email (булево) и валидными совпадающими паролями.
        using var response = await client.PostAsync(
            RegisterEndpoint,
            new StringContent(
                "{\"fullName\":123,\"login\":null,\"email\":true," +
                "\"password\":\"Passw0rd!\",\"repeatPassword\":\"Passw0rd!\"}",
                Encoding.UTF8,
                "application/json"));

        // then: 400 'Данные заполнены неверно' (не 500, не ошибка десериализации).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");

        // then: не-строковые поля отработали как пустые строки (required),
        //       строковые пароли валидны — ошибок password/repeatPassword нет.
        Assert.True(
            root.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        BodyAssertions.HasExactlyProperties(errors, "fullName", "login", "email");
        B05ContractAsserts.SingleFieldErrorIs(root, "fullName", "Заполните поле");
        B05ContractAsserts.SingleFieldErrorIs(root, "login", "Заполните поле");
        B05ContractAsserts.SingleFieldErrorIs(root, "email", "Заполните поле");

        // then: пользователь не создан; дериваций KDF нет (Δkdf=0).
        Assert.Equal(studentsBefore, users.ListStudents().Count);
        Assert.Equal(kdfBefore, TotalDerivations(kdfCounter));
    }

    /// <summary>Сумма счётчика дериваций по всем меткам вызывателей (Δkdf, FR-005).</summary>
    private static long TotalDerivations(IKdfCounter counter) =>
        counter.Snapshot().Values.Sum();
}
