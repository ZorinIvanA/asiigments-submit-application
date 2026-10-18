using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-178 «Регистрация: пароль длины 8 (зона 8–128), нарушающий ровно одно
/// композиционное правило — 400 с единственным текстом словаря» (boundary, P1,
/// FR-006/FR-023; закрывает приёмочную дыру СКЕП2-ISS-001: прежние негативные
/// парольные стимулы использовали длину 3 или 129, зона 8–128 с нарушением
/// ровно одного композиционного правила не стимулировалась).
///
/// given: логины cmp01..cmp03 и email cmp01@example.com..cmp03@example.com
///        свободны (демо-сид логинов student01..32 и teacher с кейсовыми не
///        пересекается); регистрационный лимитер учитывает ЛЮБОЙ запрос ДО
///        валидации (FR-004, 5/3600с на IP) — счётчики политики register
///        сбрасываются перед каждым запросом тестовым швом IRateLimitStore
///        (паритет с TS-175); счётчик KDF сброса не имеет (IKdfCounter —
///        только Snapshot/Increment, ADR-007 «тесты считают Δ между
///        снимками») — перед КАЖДЫМ запросом перефиксируется базовый снимок,
///        Δkdf по каждой метке вызывателя обязана быть 0.
/// when:  три POST /api/v1/auth/register с password длины ровно 8, нарушающим
///        ровно одно композиционное правило: 'Abcdefg!' (нет цифры),
///        '1234567!' (нет буквы), 'Abcdefg1' (нет спецзнака); в каждом
///        repeatPassword дословно равен password; fullName/login/email валидны
///        и различны между запросами.
/// then:  все три — 400 «Данные заполнены неверно»; errors.password — ровно
///        ОДИН текст нарушенного правила (digit/letter/special, дословно из
///        словаря «Текстов ошибок полей», FR-023); текст
///        «Пароль должен содержать не менее 8 символов» отсутствует во всех
///        трёх ответах (длина 8 включительно валидна — ветка min не
///        срабатывает); errors.repeatPassword отсутствует (повтор дословно
///        совпадает); пользователь не создан; Δkdf=0 на каждый запрос
///        (валидация — до проверок уникальности и создания, KDF только при
///        создании). Реализация, применяющая композиционные правила только к
///        паролям короче 8, заваливает этот кейс.
///
/// Примечание зоны: текущая редакция кейса (переиздание a-132 по сверке
/// дерева) закрепляет зону-владельца tests/integration/B-06 — зона test_zone
/// батча B-06; прежнее закрепление префикса Ts178 за B-03 снято (файлов Ts178
/// в B-03 нет). Файл размещён в зоне батча, when/then исполнены дословно.
/// </summary>
public sealed class Ts178_PasswordCompositionAtMinLengthTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    /// <summary>Тексты словаря «Текстов ошибок полей» (ErrorTexts, FR-023).</summary>
    private const string ExpectedDigitText = "Пароль должен содержать хотя бы одну цифру";

    private const string ExpectedLetterText = "Пароль должен содержать хотя бы одну букву";
    private const string ExpectedSpecialText = "Пароль должен содержать хотя бы один специальный знак";
    private const string ForbiddenMinLengthText = "Пароль должен содержать не менее 8 символов";

    private readonly B06SubmissionsWebAppFactory _factory;

    /// <summary>Базовый снимок KDF, перефиксируемый перед каждым запросом кейса.</summary>
    private IReadOnlyDictionary<string, long>? _kdfBefore;

    public Ts178_PasswordCompositionAtMinLengthTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_PasswordLength8WithSingleBrokenRule_SingleDictionaryText_NoUser_NoKdf()
    {
        // given: логины cmp01..cmp03 и email cmp01@example.com..cmp03@example.com
        // свободны; счётчик KDF — только шов снимков (Δ между снимками).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(users.GetByLogin("cmp01"));
        Assert.Null(users.GetByLogin("cmp02"));
        Assert.Null(users.GetByLogin("cmp03"));
        Assert.Null(users.GetByEmail("cmp01@example.com"));
        Assert.Null(users.GetByEmail("cmp02@example.com"));
        Assert.Null(users.GetByEmail("cmp03@example.com"));

        // when (1): password 'Abcdefg!' — длина ровно 8, есть буквы и спецзнак
        // '!', НЕТ цифры; repeatPassword дословно равен; прочие поля валидны.
        Assert.Equal(8, "Abcdefg!".Length);
        using var noDigit = await RegisterAsync(
            fullName: "Тест Пароль Без Цифры",
            login: "cmp01",
            email: "cmp01@example.com",
            password: "Abcdefg!");

        // then (1): 400 «Данные заполнены неверно»; errors — единственное поле
        // password с ровно ОДНИМ текстом digit; min-текста нет;
        // Δkdf=0; пользователь не создан.
        await AssertRejectedWithSinglePasswordErrorAsync(noDigit, ExpectedDigitText);
        AssertKdfUnchanged();
        Assert.Null(users.GetByLogin("cmp01"));
        Assert.Null(users.GetByEmail("cmp01@example.com"));

        // when (2): password '1234567!' — длина ровно 8, есть цифры и '!',
        // НЕТ буквы.
        Assert.Equal(8, "1234567!".Length);
        using var noLetter = await RegisterAsync(
            fullName: "Тест Пароль Без Буквы",
            login: "cmp02",
            email: "cmp02@example.com",
            password: "1234567!");

        // then (2): 400, errors.password ровно ['Пароль должен содержать хотя бы
        // одну букву']; Δkdf=0; пользователь не создан.
        await AssertRejectedWithSinglePasswordErrorAsync(noLetter, ExpectedLetterText);
        AssertKdfUnchanged();
        Assert.Null(users.GetByLogin("cmp02"));
        Assert.Null(users.GetByEmail("cmp02@example.com"));

        // when (3): password 'Abcdefg1' — длина ровно 8, есть буквы и цифра,
        // НЕТ спецзнака.
        Assert.Equal(8, "Abcdefg1".Length);
        using var noSpecial = await RegisterAsync(
            fullName: "Тест Пароль Без Спецзнака",
            login: "cmp03",
            email: "cmp03@example.com",
            password: "Abcdefg1");

        // then (3): 400, errors.password ровно ['Пароль должен содержать хотя бы
        // один специальный знак']; Δkdf=0; пользователь не создан.
        await AssertRejectedWithSinglePasswordErrorAsync(noSpecial, ExpectedSpecialText);
        AssertKdfUnchanged();
        Assert.Null(users.GetByLogin("cmp03"));
        Assert.Null(users.GetByEmail("cmp03@example.com"));
    }

    /// <summary>
    /// Общие then-шаги одного негативного запроса: 400 «Данные заполнены
    /// неверно»; errors содержит РОВНО ключ password с ровно одним текстом
    /// нарушенного правила (=> errors.repeatPassword отсутствует); текст
    /// «Пароль должен содержать не менее 8 символов» отсутствует во ВСЕХ
    /// ошибках ответа.
    /// </summary>
    private static async Task AssertRejectedWithSinglePasswordErrorAsync(
        HttpResponseMessage response,
        string expectedPasswordError)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message", "errors");
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");

        var errors = root.GetProperty("errors");
        BodyAssertions.HasExactlyProperties(errors, "password");
        BodyAssertions.ErrorFieldIsExactly(root, "password", expectedPasswordError);

        foreach (var field in errors.EnumerateObject())
        {
            foreach (var text in field.Value.EnumerateArray())
            {
                Assert.NotEqual(ForbiddenMinLengthText, text.GetString());
            }
        }
    }

    /// <summary>
    /// POST /api/v1/auth/register кейса: перед КАЖДЫМ запросом счётчики политики
    /// register сбрасываются тестовым швом IRateLimitStore (given: TryAcquire
    /// учитывает любой запрос ДО валидации, 5/3600с на IP) и перефиксируется
    /// базовый снимок KDF (Δkdf считается от снимка непосредственно перед
    /// запросом).
    /// </summary>
    private async Task<HttpResponseMessage> RegisterAsync(
        string fullName,
        string login,
        string email,
        string password)
    {
        var store = _factory.Services.GetRequiredService<IRateLimitStore>();
        foreach (var key in store.GetKeys(RateLimitPolicies.Register))
        {
            Assert.True(store.Remove(RateLimitPolicies.Register, key));
        }

        _kdfBefore = KdfSnapshot();
        return await ApiRequests.RegisterAsync(
            _factory.CreateClient(),
            fullName,
            login,
            email,
            password,
            repeatPassword: password);
    }

    /// <summary>Снимок счётчика KDF хоста (тестовый шов IKdfCounter, IF-002).</summary>
    private IReadOnlyDictionary<string, long> KdfSnapshot() =>
        _factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>Δkdf=0 по каждой метке вызывателя между базовым и текущим снимками.</summary>
    private void AssertKdfUnchanged()
    {
        var before = _kdfBefore ?? throw new InvalidOperationException("Базовый снимок KDF не зафиксирован.");
        var after = KdfSnapshot();
        foreach (var caller in KdfCallers.All)
        {
            var beforeValue = before.TryGetValue(caller, out var b) ? b : 0;
            var afterValue = after.TryGetValue(caller, out var a) ? a : 0;
            Assert.True(
                beforeValue == afterValue,
                $"Ожидался Δkdf[{caller}]=0, фактически {afterValue - beforeValue} (было {beforeValue}, стало {afterValue}).");
        }
    }
}
