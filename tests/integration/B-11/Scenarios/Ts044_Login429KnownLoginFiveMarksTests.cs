using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-044 (P0, negative; FR-007, FR-004) «Вход: 429 на известном логине — KDF
/// выполнен, метка не дописана».
/// given: 5 меток ключа 'teacher|IP' в текущем окне (созданы 5 неудачными
///        входами при фиктивном времени: m1 = T0, m2..m5 — у границы окна);
///        счётчик KDF обнулён.
/// when:  POST {login:'teacher', password:'nope123!'}.
/// then:  429 'Слишком много попыток. Повторите позже'; Δkdf=1 (KDF до
///        ShouldBlock); число меток осталось 5 (FR-007 AC «429 на известном
///        логине»). «Меток осталось 5» фиксируется поведенчески: на T0+61 с
///        (m1 вне окна, живы ровно 4 метки) следующая неудача допускается —
///        401; будь меток шесть (скрытая метка от 429), попытка осталась бы
///        заблокированной (429).
/// </summary>
public sealed class Ts044_Login429KnownLoginFiveMarksTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS044_Login_429OnKnownLogin_PerformsSingleKdfAndWritesNoMark()
    {
        // given: 5 меток 'teacher|IP' в текущем окне (5 неудачных входов при
        // фиктивном времени): m1 = T0, затем 59 с простоя и четыре метки по
        // 100 мс у границы окна; счётчик KDF обнулён снимком.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        using var client = HostClients.Create(_factory);

        var firstMarkAt = await B11LoginMarks.FailOnceAsync(_factory.Time, client, "teacher", "nope123!");
        _factory.Time.Advance(TimeSpan.FromSeconds(59));
        _ = await B11LoginMarks.FailManyAsync(_factory.Time, client, "teacher", "nope123!", 4, TimeSpan.FromMilliseconds(100));

        // when: 6-я неудача (ShouldBlock=true) при обнулённом снимком счётчике.
        var before = kdf.Snapshot();
        using var blocked = await HostClients.LoginAsync(client, "teacher", "nope123!");
        var after = kdf.Snapshot();

        // then: 429 'Слишком много попыток. Повторите позже'; Δkdf=1 — KDF
        // выполнен ДО ShouldBlock.
        await ApiAssert.AssertMessageAsync(
            blocked,
            HttpStatusCode.TooManyRequests,
            "Слишком много попыток. Повторите позже",
            exactSingleMessageProperty: true);
        Assert.Equal(1L, B11Kdf.TotalDelta(before, after));

        // then: число меток осталось 5 (429 метку НЕ дописывает): на T0+61 с
        // первая метка вне окна — живы ровно четыре, следующая неудача
        // допускается (401); скрытая метка от 429 оставила бы окно исчерпанным
        // (429).
        _factory.Time.SetUtcNow(firstMarkAt + TimeSpan.FromSeconds(61));
        using var probe = await HostClients.LoginAsync(client, "teacher", "nope123!");
        await ApiAssert.AssertMessageAsync(
            probe,
            HttpStatusCode.Unauthorized,
            "Неверный логин или пароль",
            exactSingleMessageProperty: true);
    }
}
