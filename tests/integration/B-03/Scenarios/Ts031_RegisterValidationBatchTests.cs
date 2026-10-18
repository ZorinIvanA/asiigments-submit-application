using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-031 «Регистрация: пакет ошибок валидации по всем полям сразу» (FR-006 AC
/// «Пакет ошибок валидации», FR-023; P0): given — хранилище в любом состоянии
/// (состав зафиксирован счётчиком студентов), счётчик KDF обнулён; when — POST
/// /auth/register {fullName:'Иванов Иван', login:'иван', email:'abc',
/// password:'abc', repeatPassword:'xyz'}; then — 400; message 'Данные заполнены
/// неверно'; errors.login содержит 'Логин может содержать только латинские буквы,
/// цифры, точку, дефис и подчёркивание'; errors.email — 'Введите корректный email';
/// errors.password — ровно 3 текста: 'Пароль должен содержать не менее 8 символов',
/// 'Пароль должен содержать хотя бы одну цифру', 'Пароль должен содержать хотя бы
/// один специальный знак'; errors.repeatPassword — 'Пароли не совпадают'; Δkdf=0;
/// пользователь не создан.
/// </summary>
public sealed class Ts031_RegisterValidationBatchTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts031_RegisterValidationBatchTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_AllFieldsInvalid_CollectsEveryFieldErrorWithoutKdfOrUser()
    {
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.31.0.1");
        var studentsBefore = B03UserSeed.CountStudents(_factory);

        // when: все поля невалидны одновременно (кириллица в логине, формат email,
        // короткий пароль без цифры и спецзнака, несовпадающий повтор).
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Иванов Иван",
            login: "иван",
            email: "abc",
            password: "abc",
            repeatPassword: "xyz");
        var after = kdf.Snapshot();

        // then: 400 VALIDATION с пакетом ошибок по всем полям сразу.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (все поля невалидны)");
        ResponseAssert.MessageIs(body.RootElement, "Данные заполнены неверно");
        ResponseAssert.FieldErrorsExactly(
            body.RootElement,
            "login",
            "Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "email", "Введите корректный email");
        ResponseAssert.FieldErrorsSameElements(
            body.RootElement,
            "password",
            "Пароль должен содержать не менее 8 символов",
            "Пароль должен содержать хотя бы одну цифру",
            "Пароль должен содержать хотя бы один специальный знак");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "repeatPassword", "Пароли не совпадают");

        // then: Δkdf=0 — валидация выполняется до создания и KDF.
        Assert.Equal(0, B03Kdf.TotalDelta(before, after));

        // then: пользователь не создан (состав хранилища не изменился).
        Assert.Equal(studentsBefore, B03UserSeed.CountStudents(_factory));
    }
}
