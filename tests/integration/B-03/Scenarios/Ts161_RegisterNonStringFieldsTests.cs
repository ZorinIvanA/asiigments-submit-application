using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-161 «Регистрация: не-строковые значения полей обрабатываются как пустые
/// строки» (negative, FR-006/FR-023): given — регистрационный лимит не исчерпан
/// (свежая фикстура класса; собственный IP кейса — <see cref="B03HostFactory"/>);
/// счётчик KDF снимком до запроса (Δkdf между снимками, <see cref="B03Kdf"/>);
/// пользователь с такими login/email не создаётся в этом прогоне (login/email во
/// входе — не строковые значения); when — POST /api/v1/auth/register
/// {fullName:123 (JSON-число), login:null, email:true (JSON-булево),
/// password:'Passw0rd!', repeatPassword:'Passw0rd!'}; then — 400 'Данные заполнены
/// неверно' (не 500 и не ошибка десериализации); errors.fullName=['Заполните
/// поле'], errors.login=['Заполните поле'], errors.email=['Заполните поле'] —
/// не-строковые значения трактуются как пустые строки с правилами required;
/// ошибок password/repeatPassword нет (строковые значения валидны и совпадают);
/// пользователь не создан; Δkdf=0.
/// </summary>
public sealed class Ts161_RegisterNonStringFieldsTests : IClassFixture<B03HostFactory>
{
    /// <summary>Собственный IP кейса: регистрационный лимит 5/час ключуется по IP (FR-004).</summary>
    private const string CaseIp = "10.3.161.10";

    private readonly B03HostFactory _factory;

    public Ts161_RegisterNonStringFieldsTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task NonStringFieldValues_AreTreatedAsEmptyStringsWithoutKdfOrUserCreation()
    {
        // given: снимки состава студентов и суммарного счётчика KDF до запроса;
        //        регистрационный лимит не исчерпан (свежая фикстура, свой IP).
        var kdf = B03Kdf.Resolve(_factory.Services);
        var studentsBefore = B03UserSeed.CountStudents(_factory);
        var kdfBefore = kdf.Snapshot();

        using var client = B03RegisterApi.CreateClient(_factory, CaseIp);

        // when: регистрация с нестроковыми fullName (JSON-число), login (null),
        //       email (JSON-булево) и валидными совпадающими паролями.
        using var response = await B03RegisterApi.PostRawAsync(
            client,
            "{\"fullName\":123,\"login\":null,\"email\":true," +
            "\"password\":\"Passw0rd!\",\"repeatPassword\":\"Passw0rd!\"}");

        // then: 400 'Данные заполнены неверно' (не 500 и не ошибка десериализации).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response,
            HttpStatusCode.BadRequest,
            "POST /api/v1/auth/register с нестроковыми значениями полей");
        ResponseAssert.MessageIs(body.RootElement, "Данные заполнены неверно");

        // then: errors — ровно ключи fullName/login/email (посторонних нет,
        //       в т.ч. ошибок password/repeatPassword нет), каждый —
        //       ['Заполните поле'] (не-строковые значения как пустые строки).
        Assert.Equal(
            new[] { "email", "fullName", "login" },
            ErrorKeys(body.RootElement).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        ResponseAssert.FieldErrorsExactly(body.RootElement, "fullName", "Заполните поле");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "login", "Заполните поле");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "email", "Заполните поле");

        // then: пользователь не создан; дериваций KDF нет (Δkdf=0).
        Assert.Equal(studentsBefore, B03UserSeed.CountStudents(_factory));
        Assert.Equal(0, B03Kdf.TotalDelta(kdfBefore, kdf.Snapshot()));
    }

    /// <summary>Перечень ключей объекта errors конверта (состав полевых ошибок).</summary>
    private static string[] ErrorKeys(JsonElement body) =>
        body.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToArray();
}
