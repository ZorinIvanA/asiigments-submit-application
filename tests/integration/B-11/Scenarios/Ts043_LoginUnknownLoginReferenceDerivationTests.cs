using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-043 (P0, negative; FR-007, FR-005) «Вход: неизвестный логин — тот же 401,
/// эталонная деривация».
/// given: Пользователя с lower(login)='ghost' нет; меток нет; счётчик KDF
///        обнулён.
/// when:  POST {login:'ghost', password:'whatever1!'}.
/// then:  401 'Неверный логин или пароль'; Δkdf=1 (VerifyReference); метка по
///        ключу 'ghost|IP' записана (FR-007 AC «Неизвестный логин»). Метка
///        фиксируется поведенчески: ещё четыре неудачи ghost допускаются,
///        шестая → 429.
/// </summary>
public sealed class Ts043_LoginUnknownLoginReferenceDerivationTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS043_Login_UnknownLogin_ReturnsSameUnified401WithReferenceKdfAndRecordsMark()
    {
        // given: пользователя с lower(login)='ghost' нет (свежий хост: демо-сид
        // сеет только teacher); меток нет; счётчик KDF обнулён снимком.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        using var client = HostClients.Create(_factory);

        // when: POST {login:'ghost', password:'whatever1!'}.
        var before = kdf.Snapshot();
        using var response = await HostClients.LoginAsync(client, "ghost", "whatever1!");
        var after = kdf.Snapshot();

        // then: 401 'Неверный логин или пароль' (тот же единый текст); Δkdf=1 —
        // эталонная деривация VerifyReference.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Неверный логин или пароль",
            exactSingleMessageProperty: true);
        Assert.Equal(1L, B11Kdf.TotalDelta(before, after));

        // then: метка по ключу 'ghost|IP' записана: четыре следующих неудачи ghost
        // допускаются (всего в окне 5 меток по ключу ghost), шестая → 429.
        for (var i = 0; i < 4; i++)
        {
            using var failure = await HostClients.LoginAsync(client, "ghost", "whatever1!");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var sixth = await HostClients.LoginAsync(client, "ghost", "whatever1!");
        await ApiAssert.AssertMessageAsync(
            sixth,
            HttpStatusCode.TooManyRequests,
            "Слишком много попыток. Повторите позже",
            exactSingleMessageProperty: true);
    }
}
