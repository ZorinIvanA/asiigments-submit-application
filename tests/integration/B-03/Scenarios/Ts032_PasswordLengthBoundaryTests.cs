using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-032 «Регистрация: граница пароля 128/129 (ISS-016)» (FR-006 AC «Верхняя
/// граница пароля (ISS-016)»; P0): given — прочие поля валидны; для первого запроса
/// логин/email свободны, для второго — другие свободные; счётчик снимается перед
/// каждым запросом; when — POST с password длиной ровно 128 (содержит цифру, букву,
/// спецзнак), затем POST с password длиной 129; then — первый 201 (Δkdf=1); второй
/// 400, errors.password=['Пароль — не более 128 символов'] (единственный текст —
/// прочие правила при длине &gt;128 не проверяются), пользователь не создан, Δkdf=0.
/// </summary>
public sealed class Ts032_PasswordLengthBoundaryTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts032_PasswordLengthBoundaryTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_PasswordExactly128_CreatesUser_Password129_RejectedWithMaxTextOnly()
    {
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.32.0.1");

        // Состав пароля: буквы + цифра + спецзнак; длина регулируется хвостом из 'a'.
        var password128 = new string('a', 125) + "Z9!";
        var password129 = new string('a', 126) + "Z9!";
        Assert.Equal(128, password128.Length);
        Assert.Equal(129, password129.Length);

        // when: первая попытка — password длиной ровно 128 (все правила выполнены).
        var before = kdf.Snapshot();
        using var created = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Сто Двадцать Восемь",
            login: "b03-pw128",
            email: "b03-pw128@example.com",
            password: password128,
            repeatPassword: password128);
        var after = kdf.Snapshot();

        // then: 201, Δkdf=1 — граница включена в допустимый диапазон.
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /api/v1/auth/register (password = 128 символов)"))
        {
            Assert.Equal("b03-pw128", body.RootElement.GetProperty("login").GetString());
        }

        Assert.Equal(1, B03Kdf.TotalDelta(before, after));

        // when: вторая попытка — password длиной 129 (другие свободные логин/email).
        var studentsBeforeSecond = B03UserSeed.CountStudents(_factory);
        var before129 = kdf.Snapshot();
        using var rejected = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Сто Двадцать Девять",
            login: "b03-pw129",
            email: "b03-pw129@example.com",
            password: password129,
            repeatPassword: password129);
        var after129 = kdf.Snapshot();

        // then: 400; errors.password — ТОЛЬКО текст верхней границы (прочие правила
        // при длине >128 не проверяются).
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            rejected, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (password = 129 символов)"))
        {
            ResponseAssert.FieldErrorsExactly(body.RootElement, "password", "Пароль — не более 128 символов");
        }

        // then: пользователь не создан, Δkdf=0.
        Assert.Equal(0, B03Kdf.TotalDelta(before129, after129));
        Assert.Equal(studentsBeforeSecond, B03UserSeed.CountStudents(_factory));
        Assert.Null(B03UserSeed.FindByLogin(_factory, "b03-pw129"));
    }
}
