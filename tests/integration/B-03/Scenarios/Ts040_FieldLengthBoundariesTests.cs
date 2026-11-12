using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B03.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-040 «Регистрация: границы длин login/email/fullName» (boundary, FR-006,
/// глоссарий «Тексты ошибок полей»; P1): given — свободные уникальные значения;
/// валидные password и repeatPassword; состояние регистрационного лимитера
/// зафиксировано — перед КАЖДОЙ попыткой метки политики register сброшены
/// (тестовый шов движка <see cref="IRateLimitStore"/>: FR-004 — 5 запросов за
/// окно 3600 с с одного IP, интерференция исключена); when — три пары POST
/// (6 попыток, по одной при чистом лимитере, с одного IP кейса): login длиной
/// 100 и 101; email длиной 254 и 255; fullName длиной 200 и 201 (каждая
/// граница отдельной попыткой с уникальными login/email); then — login=100 →
/// 201; login=101 → 400 errors.login=['Логин — от 1 до 100 символов'];
/// email=254 → 201; email=255 → 400 errors.email=['Email — не более 254
/// символов']; fullName=200 → 201; fullName=201 → 400 errors.fullName=['ФИО —
/// от 1 до 200 символов'] (ни одна попытка не 429: ожидаемый статус каждой
/// попытки проверяется явно, 429 несовместим с гейтом статуса).
/// </summary>
public sealed class Ts040_FieldLengthBoundariesTests : IClassFixture<B03HostFactory>
{
    /// <summary>Единый IP всех шести попыток: лимит 5/час (FR-004) обходится
    /// сбросом меток политики register перед каждой попыткой, а не сменой IP.</summary>
    private const string CaseIp = "10.148.0.1";

    private readonly B03HostFactory _factory;

    public Ts040_FieldLengthBoundariesTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_LengthBoundaries_Login100Email254FullName200_Accepted_Overhead_Rejected()
    {
        // Граничные значения: charset/формат валидны, длина — ровно на границе и на 1 больше.
        var login100 = new string('a', 100);
        var login101 = new string('b', 101);
        var email254 = new string('c', 242) + "@example.com";
        var email255 = new string('d', 243) + "@example.com";
        var fullName200 = new string('Ф', 200);
        var fullName201 = new string('Х', 201);
        Assert.Equal(100, login100.Length);
        Assert.Equal(101, login101.Length);
        Assert.Equal(254, email254.Length);
        Assert.Equal(255, email255.Length);
        Assert.Equal(200, fullName200.Length);
        Assert.Equal(201, fullName201.Length);

        // when/then: login=100 → 201 (при чистом лимитере).
        ResetRegisterLimiterMarks();
        using (var client = B03RegisterApi.CreateClient(_factory, CaseIp))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Логина Сто",
            login: login100,
            email: "ts040-login100@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        {
            await ResponseAssert.ParseWithStatusAsync(
                response, HttpStatusCode.Created, "POST /api/v1/auth/register (login = 100 символов)");
        }

        // when/then: login=101 → 400, errors.login — только текст длины.
        ResetRegisterLimiterMarks();
        using (var client = B03RegisterApi.CreateClient(_factory, CaseIp))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Логина Сто Один",
            login: login101,
            email: "ts040-login101@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (login = 101 символ)"))
        {
            ResponseAssert.FieldErrorsExactly(body.RootElement, "login", "Логин — от 1 до 100 символов");
        }

        // when/then: email=254 → 201 (при чистом лимитере).
        ResetRegisterLimiterMarks();
        using (var client = B03RegisterApi.CreateClient(_factory, CaseIp))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Эмеил Двести Пятьдесят Четыре",
            login: "ts040-email254",
            email: email254,
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        {
            await ResponseAssert.ParseWithStatusAsync(
                response, HttpStatusCode.Created, "POST /api/v1/auth/register (email = 254 символа)");
        }

        // when/then: email=255 → 400, errors.email — только текст длины.
        ResetRegisterLimiterMarks();
        using (var client = B03RegisterApi.CreateClient(_factory, CaseIp))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Эмеил Двести Пятьдесят Пять",
            login: "ts040-email255",
            email: email255,
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (email = 255 символов)"))
        {
            ResponseAssert.FieldErrorsExactly(body.RootElement, "email", "Email — не более 254 символов");
        }

        // when/then: fullName=200 → 201 (при чистом лимитере).
        ResetRegisterLimiterMarks();
        using (var client = B03RegisterApi.CreateClient(_factory, CaseIp))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: fullName200,
            login: "ts040-fullname200",
            email: "ts040-fullname200@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        {
            await ResponseAssert.ParseWithStatusAsync(
                response, HttpStatusCode.Created, "POST /api/v1/auth/register (fullName = 200 символов)");
        }

        // when/then: fullName=201 → 400, errors.fullName — только текст длины.
        ResetRegisterLimiterMarks();
        using (var client = B03RegisterApi.CreateClient(_factory, CaseIp))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: fullName201,
            login: "ts040-fullname201",
            email: "ts040-fullname201@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (fullName = 201 символ)"))
        {
            ResponseAssert.FieldErrorsExactly(body.RootElement, "fullName", "ФИО — от 1 до 200 символов");
        }
    }

    /// <summary>
    /// Тестовый шов движка лимитера (given кейса): перед каждой попыткой метки
    /// политики register сброшены — за окно 3600 с с одного IP проходит не более
    /// 5 запросов (FR-004), попытки кейса не конкурируют за лимит.
    /// </summary>
    private void ResetRegisterLimiterMarks()
    {
        var store = _factory.Services.GetRequiredService<IRateLimitStore>();
        foreach (var key in store.GetKeys(RateLimitPolicies.Register))
        {
            Assert.True(
                store.Remove(RateLimitPolicies.Register, key),
                $"Метка '{key}' политики '{RateLimitPolicies.Register}' не удалилась из хранилища лимитера.");
        }
    }
}
