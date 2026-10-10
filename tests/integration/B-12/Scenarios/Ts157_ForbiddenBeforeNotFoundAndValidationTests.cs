using System.Text;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-157 (P0, negative; FR-022) «Порядок отказов: 403 раньше 404 и 400».
/// given: Сессия student; валидного id не существует; тело запроса невалидно.
/// when:  PUT /labs/&lt;несуществующий-id&gt; под student с телом {number:0}.
/// then:  403; message «Доступ запрещён» — роль проверяется до валидации и
///        существования (FR-022 AC «Порядок 403 раньше 404»).
/// </summary>
public sealed class Ts157_ForbiddenBeforeNotFoundAndValidationTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    /// <summary>Гарантированно несуществующий uuid лабораторной.</summary>
    private const string UnknownLabId = "00000000-0000-0000-0000-0000000000fe";

    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task StudentPutUnknownLabWithInvalidBody_Forbidden()
    {
        // given: сессия student (DI-сид); валидного id не существует; тело невалидно.
        B12Seed.EnsureStudent(
            _factory,
            login: "b12-ts157-student",
            fullName: "Порядок Отказов Тестович",
            email: "b12-ts157@t.local");
        using var client = HostClients.CreateStudentClient(_factory, "b12-ts157-student");

        // when: PUT /labs/<несуществующий-id> под student с телом {number:0}.
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{B12AuthEndpoints.Labs}/{UnknownLabId}")
        {
            Content = new StringContent("{\"number\":0}", Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);

        // then: 403 «Доступ запрещён» — раньше 400 (валидация) и 404 (существование).
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.Forbidden, ErrorTexts.Forbidden);
    }
}
