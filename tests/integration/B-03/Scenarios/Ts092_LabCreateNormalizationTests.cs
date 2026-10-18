using LabsApp.IntegrationTests.B03.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-092 «labs: создание с нормализацией (строка-число, трим, пустая ссылка →
/// null)» (FR-017 AC «Создание» + нормализация после валидации; P0): given —
/// сессия teacher; пара (semester=1, number=21) не существует; when — POST
/// /api/v1/labs {number:'21' (строка из цифр), semester:1, content:'  Новая  ',
/// assignmentUrl:'', defenseRequired:true}; then — 201 LabDto с number=21 (int,
/// JSON-число), content='Новая', assignmentUrl=null, id≠null, defenseRequired=true.
/// </summary>
public sealed class Ts092_LabCreateNormalizationTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts092_LabCreateNormalizationTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateLab_NormalizableFields_ReturnsNormalizedLabDto()
    {
        // given: сессия teacher; пара (1,21) не существует.
        var (client, _) = B03TeacherSession.Create(_factory);
        Assert.Null(_factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(1, 21));

        // when: POST с number-строкой из цифр, content с краевыми пробелами и
        // пустой ссылкой (тело уходит на сервер дословно — нормализация серверная).
        using var response = await B03LabInputApi.PostAsync(
            client,
            new
            {
                number = "21",
                semester = 1,
                content = "  Новая  ",
                assignmentUrl = string.Empty,
                defenseRequired = true,
            });

        // then: 201 LabDto с нормализованными полями.
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.Created, "POST /api/v1/labs (number-строка, трим content, пустая ссылка)");
        Assert.Equal(21, B03LabInputApi.ReadInt(body.RootElement, "number"));
        Assert.Equal(1, B03LabInputApi.ReadInt(body.RootElement, "semester"));
        Assert.Equal("Новая", B03LabInputApi.ReadString(body.RootElement, "content"));
        Assert.Null(B03LabInputApi.ReadString(body.RootElement, "assignmentUrl"));
        Assert.True(B03LabInputApi.ReadBool(body.RootElement, "defenseRequired"));

        // then: id≠null (непустая строка-uuid).
        var id = B03LabInputApi.ReadString(body.RootElement, "id");
        Assert.True(
            id is { Length: > 0 },
            $"Ожидался непустой id в LabDto, фактически: {body.RootElement.GetRawText()}");
    }
}
