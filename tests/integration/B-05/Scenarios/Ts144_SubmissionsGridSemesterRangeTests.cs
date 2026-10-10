using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-144 «Ведомость: внедиапазонный semester — 400 (AR-002)» (boundary, FR-021).
///
/// given: teacher; Labs__MaxSemester=10 (умолчание конфигурации); groupId валиден.
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;semester=0 и
///        ?semester=99
/// then:  оба — 400 'Данные заполнены неверно',
///        errors.semester=['Семестр — число от 1 до 10'] — НЕ 200 с пустыми
///        массивами (FR-021 AC «Внедиапазонный semester — 400»).
/// </summary>
public sealed class Ts144_SubmissionsGridSemesterRangeTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts144_SubmissionsGridSemesterRangeTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SemesterBelowOneAndAboveMax_AreBothRejectedWithFieldError()
    {
        // given: валидная группа; teacher авторизован; MaxSemester=10.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: semester=0 и semester=99.
        using var belowRange = await client.GetAsync(
            $"/api/v1/submissions?groupId={ik221.Id}&semester=0&page=1");
        using var aboveRange = await client.GetAsync(
            $"/api/v1/submissions?groupId={ik221.Id}&semester=99&page=1");

        // then: оба запроса — 400 с errors.semester (не 200 с пустыми массивами).
        foreach (var response in new[] { belowRange, aboveRange })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var root = await BodyAssertions.ReadRootObjectAsync(response);
            BodyAssertions.MessageIs(root, "Данные заполнены неверно");
            B05ContractAsserts.SingleFieldErrorIs(root, "semester", "Семестр — число от 1 до 10");
        }
    }
}
