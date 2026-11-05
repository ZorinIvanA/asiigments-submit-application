using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-124 «me/submissions: внедиапазонный/нечисловой semester — 400»
/// (negative, FR-021).
///
/// given: текущий пользователь анонимного приложения — сид-преподаватель;
///        Labs__MaxSemester=10 (умолчание конфигурации).
/// when:  GET /api/v1/me/submissions?semester=11; ?semester=abc
/// then:  оба — 400 'Данные заполнены неверно' +
///        errors.semester=['Семестр — число от 1 до 10'] (не 200 с пустыми
///        массивами; строгий semester проверяется раньше ветки hasGroup)
///        (FR-021 AC «me/submissions: внедиапазонный semester — 400»).
/// </summary>
public sealed class Ts124_MySubmissionsSemesterStrictTests : IClassFixture<B05WebAppFactory>
{
    private const string MySubmissionsEndpoint = "/api/v1/me/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts124_MySubmissionsSemesterStrictTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AboveRange_AndNonNumericSemester_AreRejectedWithFieldError()
    {
        // given: анонимный клиент (текущий пользователь — сид-преподаватель);
        //        MaxSemester=10.
        using var client = HostClients.Create(_factory);

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
