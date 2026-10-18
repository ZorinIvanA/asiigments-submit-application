using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-038 «Регистрация: нестроковые поля обрабатываются как пустые строки»
/// (negative, FR-006 «Не-строковые значения полей обрабатываются как пустые строки»;
/// P0): given — логин/email свободны, счётчик KDF обнулён; when — POST /auth/register
/// с нестроковыми значениями {login:123, email:{}, password:null, repeatPassword:42,
/// fullName:true}; then — 400 VALIDATION, message 'Данные заполнены неверно';
/// errors.login/errors.email/errors.fullName — ['Заполните поле'] (пустая строка
/// невалидна как required); errors.repeatPassword — ['Заполните поле'] (нормализованные
/// password и repeatPassword равны, 'Пароли не совпадают' НЕ возникает); errors.password
/// — непустой массив только из текстов словаря «Тексты ошибок полей», допустимы ровно
/// два исхода: ['Заполните поле'] ИЛИ все 4 текста состава (min/digit/letter/special);
/// пользователь не создан; Δkdf=0.
/// </summary>
public sealed class Ts038_NonStringFieldsAsEmptyTests : IClassFixture<B03HostFactory>
{
    /// <summary>
    /// Допустимый исход A: passwordRules пропускает пустое значение, обязательность
    /// даёт required (эталон src/client/app/shared/validation/validators.ts).
    /// </summary>
    private static readonly string[] OutcomeRequiredOnly = ["Заполните поле"];

    /// <summary>
    /// Допустимый исход B: пустая строка &lt;8 — все 4 текста состава сразу (буква
    /// FR-006); конфликт прочтений зафиксирован в coverage_check.scenario_gaps (п.9).
    /// </summary>
    private static readonly string[] OutcomeAllFourRules =
    [
        "Пароль должен содержать не менее 8 символов",
        "Пароль должен содержать хотя бы одну цифру",
        "Пароль должен содержать хотя бы одну букву",
        "Пароль должен содержать хотя бы один специальный знак",
    ];

    /// <summary>
    /// Тексты словаря «Тексты ошибок полей», допустимые в errors.password
    /// (password-тексты + required); всё прочее — нарушение словаря.
    /// </summary>
    private static readonly string[] DictionaryPasswordTexts =
    [
        "Заполните поле",
        "Пароль должен содержать не менее 8 символов",
        "Пароль должен содержать хотя бы одну цифру",
        "Пароль должен содержать хотя бы одну букву",
        "Пароль должен содержать хотя бы один специальный знак",
        "Пароли не совпадают",
        "Пароль — не более 128 символов",
    ];

    private readonly B03HostFactory _factory;

    public Ts038_NonStringFieldsAsEmptyTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_NonStringFieldValues_AreTreatedAsEmptyStringsWithoutKdfOrUser()
    {
        var kdf = B03Kdf.Resolve(_factory.Services);
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.38.0.1");
        var studentsBefore = B03UserSeed.CountStudents(_factory);

        // when: все обязательные поля — нестроковые JSON-значения.
        var before = kdf.Snapshot();
        using var response = await B03RegisterApi.PostRawAsync(
            client,
            "{\"login\":123,\"email\":{},\"password\":null,\"repeatPassword\":42,\"fullName\":true}");
        var after = kdf.Snapshot();

        // then: 400 VALIDATION с сообщением конверта (не 500 и не ошибка десериализации).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (нестроковые значения полей)");
        ResponseAssert.MessageIs(body.RootElement, "Данные заполнены неверно");

        // then: триммируемые обязательные поля — пустая строка невалидна как required.
        ResponseAssert.FieldErrorsExactly(body.RootElement, "login", "Заполните поле");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "email", "Заполните поле");
        ResponseAssert.FieldErrorsExactly(body.RootElement, "fullName", "Заполните поле");

        // then: нормализованные password и repeatPassword равны ('' и '') — mismatch
        // НЕ возникает, обязательность повтора даёт required.
        ResponseAssert.FieldErrorsExactly(body.RootElement, "repeatPassword", "Заполните поле");

        // then: errors.password — непустой массив только из текстов словаря, равный
        // одному из двух допустимых исходов.
        var errors = default(JsonElement);
        var passwordErrors = default(JsonElement);
        Assert.True(
            body.RootElement.TryGetProperty("errors", out errors)
            && errors.TryGetProperty("password", out passwordErrors)
            && passwordErrors.ValueKind == JsonValueKind.Array,
            $"Ожидался массив errors.password в теле ответа, фактически: {body.RootElement.GetRawText()}");
        var actual = passwordErrors.EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.NotEmpty(actual);
        Assert.All(
            actual,
            text => Assert.True(
                DictionaryPasswordTexts.Contains(text),
                $"errors.password содержит текст вне словаря «Тексты ошибок полей»: '{text}'; тело: {body.RootElement.GetRawText()}"));
        Assert.True(
            SameElements(actual, OutcomeRequiredOnly) || SameElements(actual, OutcomeAllFourRules),
            $"errors.password должен быть ['Заполните поле'] либо всеми 4 текстами состава, фактически " +
            $"[{string.Join(", ", actual.Select(text => $"'{text}'"))}]; тело: {body.RootElement.GetRawText()}");

        // then: пользователь не создан; Δkdf=0.
        Assert.Equal(studentsBefore, B03UserSeed.CountStudents(_factory));
        Assert.Equal(0, B03Kdf.TotalDelta(before, after));
    }

    /// <summary>Сравнение составов без учёта порядка (кейс не нормирует порядок текстов).</summary>
    private static bool SameElements(string[] actual, string[] expected) =>
        actual.Length == expected.Length
        && !expected.Except(actual).Any()
        && !actual.Except(expected).Any();
}
