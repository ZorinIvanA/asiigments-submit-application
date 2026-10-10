using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-049 (P1, boundary; FR-007, FR-003) «Вход: скольжение окна 60 с — неудача в
/// t0+61 с даёт 401» (актуальная нумерация кейсов батча B-10; в тайле входа
/// предыдущей нумерации скольжение окна login-лимитера не покрыто).
/// given: 5-я метка 'teacher|IP' записана в момент t0 (5 неудачных входов при
///        фиктивном времени — время движется только явно).
/// when:  Неудачный вход (неверный пароль) в момент t0+61000 мс (инжектируемые
///        часы, Advance).
/// then:  401 и метка записана — не 429: окно 60 с скользнуло (все метки t0
///        старше порога now−60000 мс и вычищены, отказ допускается и дописывает
///        метку) (AC FR-007 «Окно скользит»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts049_LoginWindowSlidingTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.49";

    /// <summary>Окно политики login (FR-004: 60 с) и шаг за его границу (кейс: +61 с).</summary>
    private static readonly TimeSpan LoginWindow = TimeSpan.FromSeconds(60);

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS049_Login_FailureAtT0Plus61Seconds_SlidesWindowAndReturns401Not429()
    {
        // given: 5-я метка 'teacher|IP' записана в момент t0 (5 неудач; свежий хост,
        // фиктивное время стоит в t0 и движется только явно).
        _ = _factory.Services;
        using var client = B10AuthRequests.Create(_factory, TestIp);
        for (var i = 0; i < 5; i++)
        {
            using var seed = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, seed.StatusCode);
        }

        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));

        // when: неудачный вход (неверный пароль) в момент t0+61000 мс — метки t0
        // СТРОГО старше порога now−60000 мс (61000 > 60000) и покидают окно.
        _factory.Time.Advance(LoginWindow.Add(TimeSpan.FromSeconds(1)));
        using var response = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");

        // then: 401 'Неверный логин или пароль' — не 429: окно 60 с скользнуло.
        await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Неверный логин или пароль");

        // then: метка записана — пять меток t0 вычищены из окна, отказ допущен и
        // дописал ровно одну свежую метку (FR-003: вычистка перед проверкой).
        Assert.Equal(1, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));
    }
}
