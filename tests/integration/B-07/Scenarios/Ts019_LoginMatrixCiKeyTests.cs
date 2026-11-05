using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-019 «Матрица login: ключ 'lower(trim(login))|IP', окно 60с, лимит 5»
/// (negative, FR-004 + FR-007, P0).
///
/// given: хост Development; пользователь teacher/teacher123! существует (сид
///        фикстуры); лимитер пуст (свежая фикстура); IP тестового клиента
///        фиксирован.
/// when:  5 неудачных POST /api/v1/auth/login с вариантами login: 'Teacher',
///        ' teacher ', 'TEACHER', 'teachER', 'teacher' (пароль неверный); затем
///        6-я неудачная попытка с 'teacher'; отдельно — неудачная попытка с
///        login 'teacher2'.
/// then:  первые 5 — 401 (все варианты пишут метки в один ключ 'teacher|IP' —
///        нижний регистр и трим); 6-я с 'teacher' — 429 'Слишком много попыток.
///        Повторите позже' (лимит 5 за 60с); попытка с 'teacher2' — 401, не 429
///        (ключи независимы).
/// </summary>
public sealed class Ts019_LoginMatrixCiKeyTests : IClassFixture<B07RateLimitWebAppFactory>
{
    private const string ClientIp = "10.0.7.1";
    private const string WrongPassword = "definitely-wrong-pass";
    private const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    private readonly B07RateLimitWebAppFactory _factory;

    public Ts019_LoginMatrixCiKeyTests(B07RateLimitWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_CaseAndTrimVariantsShareKey_SixthFailure429_OtherLogin401()
    {
        // given: фиксированный IP клиента; лимитер пуст; teacher/teacher123! существует.
        using var client = B07AuthClients.CreateClientWithIp(_factory, ClientIp);

        // when: 5 неудачных входов с вариантами login (пароль неверный).
        string[] variants = ["Teacher", " teacher ", "TEACHER", "teachER", "teacher"];
        foreach (var variant in variants)
        {
            using var failure = await B07AuthClients.PostLoginAsync(client, variant, WrongPassword);

            // then: каждый вариант — 401 (метка в общий ключ 'teacher|IP').
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        // when: 6-я неудачная попытка с 'teacher'.
        using var sixth = await B07AuthClients.PostLoginAsync(client, "teacher", WrongPassword);

        // then: 429 'Слишком много попыток. Повторите позже' (лимит 5 за 60с).
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(envelope, RateLimitedMessage);

        // when: отдельно неудачная попытка с login 'teacher2'.
        using var otherLogin = await B07AuthClients.PostLoginAsync(client, "teacher2", WrongPassword);

        // then: 401, не 429 — ключи независимы.
        Assert.Equal(HttpStatusCode.Unauthorized, otherLogin.StatusCode);
    }
}
