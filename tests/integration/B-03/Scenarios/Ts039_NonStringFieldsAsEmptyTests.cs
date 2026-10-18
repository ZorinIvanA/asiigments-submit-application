using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-039 «Регистрация: нестроковые значения полей обрабатываются как пустые
/// строки» (negative, FR-006 «Не-строковые значения полей обрабатываются как
/// пустые строки»): given — прочие поля запроса валидны; when — POST с login
/// как JSON-числом 123; отдельно POST (другая попытка) с password как
/// JSON-числом 456 (repeatPassword тоже 456 — значения совпадают); then —
/// первая попытка — 400, errors.login=['Заполните поле'] (123 → '' после
/// трима); вторая — 400, errors.password содержит 'Пароль должен содержать
/// не менее 8 символов' (456 → '' длины 0 &lt; 8) — единый детерминированный
/// исход для каждой попытки (не 500 и не ошибка десериализации).
/// </summary>
public sealed class Ts039_NonStringFieldsAsEmptyTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts039_NonStringFieldsAsEmptyTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_NonStringLogin_IsEmptyString()
    {
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.147.0.1");
        var studentsBefore = B03UserSeed.CountStudents(_factory);

        // when: login — JSON-число 123, прочие поля валидны.
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostRawAsync(
            client,
            "{\"fullName\":\"Нестроковый Логин\",\"login\":123,\"email\":\"ts039-login@example.com\"," +
            "\"password\":\"Passw0rd!\",\"repeatPassword\":\"Passw0rd!\"}");
        var after = kdf.Snapshot();

        // then: 400; 123 → пустая строка после трима — required-текст по login;
        // единый детерминированный исход (не 500 и не ошибка десериализации).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (login — JSON-число 123)");
        ResponseAssert.MessageIs(body.RootElement, "Данные заполнены неверно");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "login", "Заполните поле");

        // then: попытка не создаёт пользователя и не дериваций (Δkdf=0, FR-004(б)).
        Assert.Equal(studentsBefore, B03UserSeed.CountStudents(_factory));
        Assert.Equal(0, B03Kdf.TotalDelta(before, after));
    }

    [Fact]
    public async Task Register_NonStringPassword_IsEmptyString_ShorterThanMin()
    {
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.147.0.2");
        var studentsBefore = B03UserSeed.CountStudents(_factory);

        // when: password и repeatPassword — JSON-числа 456 (значения совпадают:
        // 456 → ''), прочие поля валидны.
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostRawAsync(
            client,
            "{\"fullName\":\"Нестроковый Пароль\",\"login\":\"ts039-num-password\",\"email\":\"ts039-password@example.com\"," +
            "\"password\":456,\"repeatPassword\":456}");
        var after = kdf.Snapshot();

        // then: 400; 456 → пустая строка длины 0 < 8 — errors.password СОДЕРЖИТ
        // текст минимума (кейс требует «содержит»: состав остальных правил пароля
        // — предмет словаря, не этого кейса); единый детерминированный исход.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (password — JSON-число 456)");
        ResponseAssert.MessageIs(body.RootElement, "Данные заполнены неверно");
        Assert.True(
            body.RootElement.TryGetProperty("errors", out var errors)
            && errors.TryGetProperty("password", out var passwordErrors)
            && passwordErrors.ValueKind == JsonValueKind.Array
            && passwordErrors.EnumerateArray().Any(item => item.GetString() == "Пароль должен содержать не менее 8 символов"),
            $"Ожидался errors.password, содержащий 'Пароль должен содержать не менее 8 символов', фактически: {body.RootElement.GetRawText()}");

        // then: попытка не создаёт пользователя и не дериваций (Δkdf=0, FR-004(б)).
        Assert.Equal(studentsBefore, B03UserSeed.CountStudents(_factory));
        Assert.Equal(0, B03Kdf.TotalDelta(before, after));
    }
}
