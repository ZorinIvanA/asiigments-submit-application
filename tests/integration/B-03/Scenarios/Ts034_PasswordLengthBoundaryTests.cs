using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-034 «Регистрация: верхняя граница пароля 128/129 (ISS-016)» (boundary,
/// FR-006 AC «Верхняя граница пароля»; P0): given — все прочие поля валидны;
/// логин и email свободны; счётчик KDF обнулён перед каждой попыткой (Δ между
/// снимками до/после каждой попытки — <see cref="B03Kdf"/>); when — POST с
/// password длиной ровно 128 (содержит цифру, букву, спецзнак); затем POST
/// (другой логин/email) с password длиной 129; then — первый — 201 (Δkdf=1);
/// второй — 400, errors.password=['Пароль — не более 128 символов'] (только
/// этот текст: прочие правила при длине &gt;128 не проверяются), пользователь
/// не создан, Δkdf=0.
/// </summary>
public sealed class Ts034_PasswordLengthBoundaryTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts034_PasswordLengthBoundaryTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_PasswordExactly128_CreatesUser_Password129_RejectedWithMaxTextOnly()
    {
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.142.0.1");

        // Состав пароля: буквы + цифра + спецзнак; длина регулируется хвостом из 'a'.
        var password128 = new string('a', 125) + "Z9!";
        var password129 = new string('a', 126) + "Z9!";
        Assert.Equal(128, password128.Length);
        Assert.Equal(129, password129.Length);

        // when: первая попытка — password длиной ровно 128 (все правила выполнены).
        var before128 = kdf.Snapshot();
        using var created = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Сто Двадцать Восемь",
            login: "pw128boundary",
            email: "pw128boundary@example.com",
            password: password128,
            repeatPassword: password128);
        var after128 = kdf.Snapshot();

        // then: 201; Δkdf=1 — граница включена в допустимый диапазон (и суммарный
        // Δkdf по всем меткам тоже 1, FR-004(б)).
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /api/v1/auth/register (password = 128 символов)"))
        {
            Assert.Equal("pw128boundary", body.RootElement.GetProperty("login").GetString());
        }

        Assert.Equal(1, B03Kdf.CallerDelta(before128, after128, B03Kdf.RegisterCaller));
        Assert.Equal(1, B03Kdf.TotalDelta(before128, after128));

        // when: вторая попытка — password длиной 129, другой логин/email (свободны).
        var before129 = kdf.Snapshot();
        using var rejected = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Сто Двадцать Девять",
            login: "pw129boundary",
            email: "pw129boundary@example.com",
            password: password129,
            repeatPassword: password129);
        var after129 = kdf.Snapshot();

        // then: 400; errors.password — ТОЛЬКО текст верхней границы (прочие правила
        // при длине >128 не проверяются, ISS-016).
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            rejected, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (password = 129 символов)"))
        {
            ResponseAssert.MessageIs(body.RootElement, "Данные заполнены неверно");
            ResponseAssert.FieldErrorsExactly(body.RootElement, "password", "Пароль — не более 128 символов");
        }

        // then: пользователь не создан, Δkdf=0 (KDF только при успешном создании).
        Assert.Null(B03UserSeed.FindByLogin(_factory, "pw129boundary"));
        Assert.Equal(0, B03Kdf.TotalDelta(before129, after129));
    }
}
