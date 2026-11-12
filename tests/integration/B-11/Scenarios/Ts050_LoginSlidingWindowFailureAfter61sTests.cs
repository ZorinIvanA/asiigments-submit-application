using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-050 (P1, boundary; FR-007, FR-003) «Вход: окно скользит — неудача в
/// t0+61с даёт 401, не 429».
/// given: Инжектируемые часы на T0; 5 неудачных входов 'teacher' (5-я метка
///        записана в T0 — все пять меток в одном моменте).
/// when:  Перевод часов на T0+61000 мс; POST {login:'teacher',
///        password:'nope123!'}.
/// then:  401 'Неверный логин или пароль' (метка записана), не 429 — окно 60 с
///        скользит (FR-007 AC «Окно скользит»). «Метка записана» фиксируется
///        поведенчески: после этой попытки в окне ровно одна живая метка —
///        ровно четыре следующих неудачи допускаются (401), пятая упирается в
///        лимит (429); не запиши попытка метку, 429 наступила бы только шестой.
/// </summary>
public sealed class Ts050_LoginSlidingWindowFailureAfter61sTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS050_Login_FailureAtT0Plus61s_IsAdmitted401Not429_AndWritesMark()
    {
        // given: инжектируемые часы на T0 (начальное время FakeTimeProvider);
        // 5 неудачных входов 'teacher' — все метки в момент T0.
        _ = _factory.Services;
        using var client = HostClients.Create(_factory);

        _ = await B11LoginMarks.FailManyAsync(_factory.Time, client, "teacher", "nope123!", 5);

        // when: перевод часов на T0+61000 мс; неудачный вход.
        _factory.Time.Advance(TimeSpan.FromMilliseconds(61000));
        using var response = await HostClients.LoginAsync(client, "teacher", "nope123!");

        // then: 401 'Неверный логин или пароль' — все пять меток T0 вышли из
        // окна (порог строго новее now−60 с), окно скользит; не 429.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Неверный логин или пароль",
            exactSingleMessageProperty: true);

        // then: метка попытки записана — в окне ровно одна живая метка: четыре
        // следующих неудачи допускаются (401), пятая даёт 429 (лимит 5); не
        // запиши попытка метку, 429 наступила бы только шестой.
        for (var i = 0; i < 4; i++)
        {
            using var failure = await HostClients.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var fifth = await HostClients.LoginAsync(client, "teacher", "nope123!");
        await ApiAssert.AssertMessageAsync(
            fifth,
            HttpStatusCode.TooManyRequests,
            "Слишком много попыток. Повторите позже",
            exactSingleMessageProperty: true);
    }
}
