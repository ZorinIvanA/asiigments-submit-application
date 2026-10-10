using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-116 «submissions: строгий контракт semester и обязательность groupId»
/// (negative, FR-021/FR-023).
///
/// given: сессия teacher (минт, ADR-015); Labs__MaxSemester=10 (умолчание
///        конфигурации); валидный groupId существует (ИК-221 сида).
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;semester=0;
///        ?semester=99; ?semester=abc; запрос без параметра semester; запрос
///        без параметра groupId.
/// then:  внедиапазонные и нечисловой semester, а также его отсутствие —
///        400 'Данные заполнены неверно' +
///        errors.semester=['Семестр — число от 1 до 10']; отсутствие groupId —
///        400 + errors.groupId=['Заполните поле'] (строгий контракт
///        AR-002/ISS-011 — 400, а НЕ 200 с пустыми массивами).
/// </summary>
public sealed class Ts116_SubmissionsSemesterStrictContractTests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts116_SubmissionsSemesterStrictContractTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task OutOfRange_NonNumeric_AndMissingSemester_AreRejectedWithFieldError()
    {
        // given: валидная группа ИК-221; teacher авторизован; MaxSemester=10.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: semester=0; semester=99; semester=abc; semester отсутствует.
        using var belowRange = await client.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&semester=0&page=1");
        using var aboveRange = await client.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&semester=99&page=1");
        using var nonNumeric = await client.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&semester=abc&page=1");
        using var missing = await client.GetAsync(
            $"{GridEndpoint}?groupId={ik221.Id}&page=1");

        // then: все четыре — 400 'Данные заполнены неверно' с ровно
        //       errors.semester=['Семестр — число от 1 до 10'] (не 200);
        //       состав ключей errors закреплён точно (CR-003).
        foreach (var response in new[] { belowRange, aboveRange, nonNumeric, missing })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var root = await BodyAssertions.ReadRootObjectAsync(response);
            BodyAssertions.MessageIs(root, "Данные заполнены неверно");
            Assert.True(
                root.TryGetProperty("errors", out var errors),
                "В теле 400 отсутствует ключ errors.");
            BodyAssertions.HasExactlyProperties(errors, "semester");
            B05ContractAsserts.SingleFieldErrorIs(root, "semester", "Семестр — число от 1 до 10");
        }
    }

    [Fact]
    public async Task MissingGroupId_IsRejectedWithRequiredFieldError()
    {
        // given: teacher авторизован; валидный semester=1; groupId не передан.
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: GET /submissions?semester=1&page=1 (без groupId).
        using var response = await client.GetAsync($"{GridEndpoint}?semester=1&page=1");

        // then: 400 + errors.groupId=['Заполните поле'] (состав ключей errors
        //       закреплён точно — CR-003).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        Assert.True(
            root.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        BodyAssertions.HasExactlyProperties(errors, "groupId");
        B05ContractAsserts.SingleFieldErrorIs(root, "groupId", "Заполните поле");
    }
}
