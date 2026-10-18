using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-143 «Ведомость: отсутствует semester — 400» (negative, FR-021).
///
/// given: groupId указывает на существующую группу (ИК-221 сида).
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;page=1
/// then:  400; message 'Данные заполнены неверно';
///        errors.semester=['Семестр — число от 1 до 10'] — строгий контракт
///        ISS-011/AR-002 (FR-021 AC «Отсутствует semester»).
/// </summary>
public sealed class Ts143_SubmissionsGridMissingSemesterTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts143_SubmissionsGridMissingSemesterTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task MissingSemester_IsRejectedWithFieldError()
    {
        // given: валидная группа; teacher авторизован.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: запрос ведомости без semester.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={ik221.Id}&page=1");

        // then: 400 с errors.semester=['Семестр — число от 1 до 10'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        B05ContractAsserts.SingleFieldErrorIs(root, "semester", "Семестр — число от 1 до 10");
    }
}
