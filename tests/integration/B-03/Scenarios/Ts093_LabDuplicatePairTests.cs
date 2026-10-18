using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-093 «labs: дубликат пары (semester, number) — 409» (FR-017 AC «Дубликат
/// пары»; P0): given — пара (1,1) существует (DI-сид); сессия teacher; when —
/// POST /labs {number:1, semester:1, content:'X', assignmentUrl:null,
/// defenseRequired:false}; then — 409 'Лабораторная с таким номером уже есть
/// в семестре'.
/// </summary>
public sealed class Ts093_LabDuplicatePairTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts093_LabDuplicatePairTests(B03HostFactory factory)
    {
        _factory = factory;
        B03DomainSeed.AddLab(_factory, semester: 1, number: 1);
    }

    [Fact]
    public async Task CreateLab_DuplicatePair_ReturnsConflict()
    {
        // given: сессия teacher.
        var (client, _) = B03TeacherSession.Create(_factory);

        // when: POST с существующей парой (1,1).
        using var response = await B03LabInputApi.PostAsync(
            client,
            new
            {
                number = 1,
                semester = 1,
                content = "X",
                assignmentUrl = (string?)null,
                defenseRequired = false,
            });

        // then: 409 CONFLICT_LAB.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Conflict, "POST /api/v1/labs (дубликат пары (1,1))");
        ResponseAssert.MessageIs(body.RootElement, "Лабораторная с таким номером уже есть в семестре");
    }
}
