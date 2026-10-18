using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-114 (P0, negative; FR-017 «defenseRequired — строго boolean») «Лабораторные:
/// нестрогое defenseRequired отклоняется».
/// given: teacher; остальные поля LabInput валидны; пара свободна.
/// when:  три POST /labs: с defenseRequired='yes' (строка вместо boolean);
///        с defenseRequired=1 (число); без поля defenseRequired вовсе.
/// then:  все три — 400 VALIDATION; работа не создана. Текст ошибки для поля
///        словарём не определён — утверждается только статус и отсутствие создания.
/// </summary>
public sealed class Ts114_LabDefenseRequiredStrictBooleanTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS114_PostLabWithNonBooleanDefenseRequired_RejectedAndNotCreated()
    {
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when/then (1): defenseRequired='yes' (строка вместо boolean) — 400;
        // работа с парой (1,501) не создана.
        using var withString = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 501,
            semester = 1,
            content = "Строгий boolean",
            assignmentUrl = (string?)null,
            defenseRequired = "yes",
        });
        Assert.Equal(HttpStatusCode.BadRequest, withString.StatusCode);
        Assert.Null(labs.TryGetByPair(semester: 1, number: 501));

        // when/then (2): defenseRequired=1 (число) — 400; пара (1,502) свободна.
        using var withNumber = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 502,
            semester = 1,
            content = "Строгий boolean",
            assignmentUrl = (string?)null,
            defenseRequired = 1,
        });
        Assert.Equal(HttpStatusCode.BadRequest, withNumber.StatusCode);
        Assert.Null(labs.TryGetByPair(semester: 1, number: 502));

        // when/then (3): без поля defenseRequired вовсе — 400; пара (1,503) свободна.
        using var withoutField = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 503,
            semester = 1,
            content = "Строгий boolean",
            assignmentUrl = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, withoutField.StatusCode);
        Assert.Null(labs.TryGetByPair(semester: 1, number: 503));
    }
}
