using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-042 (P0, negative; FR-007, FR-005) «Вход: неизвестный логин — тот же 401,
/// эталонная деривация» (актуальная нумерация кейсов батча B-10; родственный тест
/// предыдущей нумерации — Ts041_LoginUnknownLoginReferenceTests).
/// given: Пользователя с lower(login)='ghost' не существует (демо-набор выключен,
///        сид — только teacher); меток нет; счётчик обнулён.
/// when:  POST /auth/login {login:'ghost', password:'whatever1!'}.
/// then:  401, тот же текст 'Неверный логин или пароль'; Δkdf=1 (VerifyReference);
///        метка по ключу 'ghost|IP' записана (AC FR-007 «Неизвестный логин»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts042_LoginUnknownLoginReferenceTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.42";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS042_Login_UnknownLogin_ReturnsSame401ReferenceDerivationAndWritesGhostMark()
    {
        // given: пользователя 'ghost' не существует; меток нет; счётчик обнулён.
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST /auth/login {login:'ghost', password:'whatever1!'}.
        using var response = await B10AuthRequests.LoginAsync(client, "ghost", "whatever1!");
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 401 с тем же текстом, что и при неверном пароле известного логина.
        _ = await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Неверный логин или пароль");

        // then: Δkdf=1 — эталонная деривация VerifyReference (метка 'reference'),
        // ровно одна, как и ветка Verify найденного пользователя.
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
        Assert.Equal(1L, B10AuthGates.Delta(before, after, KdfCallers.Reference));

        // then: метка по ключу 'ghost|IP' записана.
        Assert.Equal(1, B10AuthGates.LoginMarksCount(_factory, "ghost", TestIp));
    }
}
