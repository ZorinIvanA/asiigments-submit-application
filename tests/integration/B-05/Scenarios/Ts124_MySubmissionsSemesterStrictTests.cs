using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-124 «me/submissions: внедиапазонный/нечисловой semester — 400»
/// (negative, FR-021).
///
/// given: student01 авторизован (минт сессии, ADR-015); Labs__MaxSemester=10
///        (умолчание конфигурации).
/// when:  GET /api/v1/me/submissions?semester=11; ?semester=abc
/// then:  оба — 400 'Данные заполнены неверно' +
///        errors.semester=['Семестр — число от 1 до 10'] (не 200 с пустыми
///        массивами) (FR-021 AC «me/submissions: внедиапазонный semester — 400»).
/// </summary>
public sealed class Ts124_MySubmissionsSemesterStrictTests : IClassFixture<B05WebAppFactory>
{
    private const string MySubmissionsEndpoint = "/api/v1/me/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts124_MySubmissionsSemesterStrictTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AboveRange_AndNonNumericSemester_AreRejectedWithFieldError()
    {
        // given: валидная сессия student01; MaxSemester=10.
        var student01 = B05SeedLookup.StudentByLogin(_factory, "student01");
        using var client = B05MintedSessions.Create(_factory);
        B05MintedSessions.MintAccessCookie(_factory, client, student01.Id, UserRoles.Student);

        // when: semester=11 и semester=abc.
        using var aboveRange = await client.GetAsync($"{MySubmissionsEndpoint}?semester=11");
        using var nonNumeric = await client.GetAsync($"{MySubmissionsEndpoint}?semester=abc");

        // then: оба — 400 с errors.semester (не 200 с пустыми массивами);
        //       состав ключей errors закреплён точно (CR-003).
        foreach (var response in new[] { aboveRange, nonNumeric })
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
}
