using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-021 «Матрица политик: окна/лимиты и состав ключа login»
/// (happy_path, FR-004, P0).
///
/// given: зарегистрированные политики движка (DI тестового хоста); тестовый
///        хост; пользователь teacher существует (сид); меток нет.
/// when:  чтение конфигурации политик из DI: login → окно 60000 мс, лимит 5;
///        register → окно 3600000 мс, лимит 5; recovery_request → окно
///        3600000 мс, лимит 3. Поведенческая проба состава ключа login:
///        5 неудачных входов по логину 'Ghost', затем неудачный вход по
///        'ghost' (lower).
/// then:  значения окон/лимитов совпадают с матрицей FR-004; 6-й вход 'ghost'
///        → 429 (ключ 'lower(login)|IP' один и тот же для 'Ghost' и 'ghost').
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файлы прежних волн
/// зоны с совпадающим поведением (Ts018_RateLimitPolicyMatrix,
/// Ts164_LimiterPolicyMatrix) не изменялись.
/// </summary>
public sealed class Ts021_PolicyMatrixAndLoginKeyCompositionTests : IClassFixture<B09WebAppFactory>
{
    private const string TestIp = "10.0.0.71";
    private const string WrongPassword = "whatever1!";

    private readonly B09WebAppFactory _factory;

    public Ts021_PolicyMatrixAndLoginKeyCompositionTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PolicyMatrixMatchesFr004_AndLoginKeyIsSharedAcrossLoginCase()
    {
        // given/when: чтение конфигурации политик из DI (разрешение = регистрация
        // в DI; константы — конфигурация политик).
        var login = _factory.Services.GetRequiredService<LoginFailureLimiter>();
        var register = _factory.Services.GetRequiredService<RegisterLimiter>();
        var recovery = _factory.Services.GetRequiredService<RecoveryRequestLimiter>();

        // then: значения окон/лимитов совпадают с матрицей FR-004:
        // login 60000/5; register 3600000/5; recovery_request 3600000/3.
        Assert.Equal(60_000L, LoginFailureLimiter.FailureWindowMs);
        Assert.Equal(5, LoginFailureLimiter.FailureLimit);
        Assert.Equal(3_600_000L, RegisterLimiter.RegisterWindowMs);
        Assert.Equal(5, RegisterLimiter.RegisterLimit);
        Assert.Equal(3_600_000L, RecoveryRequestLimiter.RequestWindowMs);
        Assert.Equal(3, RecoveryRequestLimiter.RequestLimit);

        // given: меток нет (свежий хост фикстуры); 'ghost' не существует
        // (демо-набор выключен) — входы неудачны и учитываются лимитером.
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when: 5 неудачных входов по логину 'Ghost' (в пределах 60-с окна).
        for (var attempt = 0; attempt < LoginFailureLimiter.FailureLimit; attempt++)
        {
            using var response = await B09AuthHttp.LoginAsync(client, "Ghost", WrongPassword);
            var body = await B09Assertions.ParseObjectAsync(
                response, HttpStatusCode.Unauthorized, $"неудачный вход 'Ghost' ({attempt + 1} из 5)");
            B09Assertions.MessageIs(body, B09AuthSupport.WrongCredentialsMessage);
        }

        // then: все 5 меток в ОДНОМ ключе 'lower(login)|IP' = 'ghost|IP'.
        Assert.True(
            B09LoginMarkStore.MarksCount(_factory, "Ghost", TestIp) == 5,
            "Пять неудач 'Ghost' должны заполнить один ключ 'ghost|IP'.");

        // when: неудачный вход по 'ghost' (lower) — 6-й по тому же ключу.
        using var sixth = await B09AuthHttp.LoginAsync(client, "ghost", WrongPassword);

        // then: 429 RATE_LIMITED — ключ 'lower(login)|IP' один и тот же для
        // 'Ghost' и 'ghost'.
        var sixthBody = await B09Assertions.ParseObjectAsync(
            sixth, HttpStatusCode.TooManyRequests, "6-й вход 'ghost' (тот же ключ 'ghost|IP')");
        B09Assertions.MessageIs(sixthBody, B09AuthSupport.RateLimitedMessage);
    }
}
