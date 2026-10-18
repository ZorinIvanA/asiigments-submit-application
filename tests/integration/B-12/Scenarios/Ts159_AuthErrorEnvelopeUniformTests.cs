using System.Text.Json;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-159 (P1, nfr; FR-022, FR-023) «Тело ошибок авторизации едино и без stack trace».
/// given: Вызваны защищённые эндпойнты /api/v1/* с отказом авторизации (401 и 403).
/// when:  Разбор тел ответов.
/// then:  JSON {message:'Не авторизован'|'Доступ запрещён'}; без stack trace и
///        имён типов (FR-022 AC «Тело ошибок едино»).
/// </summary>
public sealed class Ts159_AuthErrorEnvelopeUniformTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task AuthRejectionBodies_SingleMessageProperty_NoStackTracesOrTypeNames()
    {
        // given: отказ 401 — GET /auth/me анонимно.
        using var anonymous = B12AuthSessions.CreateClient(_factory);
        using var unauthorized = await anonymous.GetAsync(B12AuthEndpoints.Me);
        var unauthorizedRaw = await unauthorized.Content.ReadAsStringAsync();

        // given: отказ 403 — GET /labs под student.
        B12Seed.EnsureStudent(
            _factory,
            login: "b12-ts159-student",
            fullName: "Конверт Ошибок Тестович",
            email: "b12-ts159@t.local");
        using var student = HostClients.CreateStudentClient(_factory, "b12-ts159-student");
        using var forbidden = await student.GetAsync(B12AuthEndpoints.Labs);
        var forbiddenRaw = await forbidden.Content.ReadAsStringAsync();

        // when/then: тела — JSON-объект ровно со свойством message, тексты дословные.
        var unauthorizedBody = await ApiAssert.AssertMessageAsync(
            unauthorized, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized,
            exactSingleMessageProperty: true);
        var forbiddenBody = await ApiAssert.AssertMessageAsync(
            forbidden, HttpStatusCode.Forbidden, ErrorTexts.Forbidden,
            exactSingleMessageProperty: true);
        Assert.Equal(JsonValueKind.Object, unauthorizedBody.ValueKind);
        Assert.Equal(JsonValueKind.Object, forbiddenBody.ValueKind);

        // then: без stack trace и имён типов.
        foreach (var raw in new[] { unauthorizedRaw, forbiddenRaw })
        {
            Assert.DoesNotContain("stacktrace", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("   at ", raw, StringComparison.Ordinal);
        }
    }
}
