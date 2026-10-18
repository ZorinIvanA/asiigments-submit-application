using LabsApp.IntegrationTests.B03.Infrastructure;
using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-094 «labs: границы семестра 1/10 валидны, 0/11 — 400 с шаблонным текстом»
/// (FR-017 AC «Границы семестра», ISS-015; P0): given — сессия teacher;
/// Labs__MaxSemester=10 (умолчание тестового хоста, фиксируется снимком конфигурации);
/// пары свободны; when — POST с semester=1; semester=10; semester=0; semester=11
/// (прочие поля валидны); then — первые два 201; последние два 400 с
/// errors.semester=['Семестр — число от 1 до 10'] (текст — шаблон от
/// Labs__MaxSemester).
/// </summary>
public sealed class Ts094_LabSemesterBoundsTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts094_LabSemesterBoundsTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateLab_SemesterBoundaries_InRangeCreatedOutOfRangeRejected()
    {
        // given: сессия teacher; граница конфигурации ровно 10 (умолчание хоста).
        var (client, _) = B03TeacherSession.Create(_factory);
        Assert.Equal(
            LabsOptions.DefaultMaxSemester,
            _factory.Services.GetRequiredService<IOptions<LabsOptions>>().Value.MaxSemester);

        // when/then: semester=1 — нижняя граница включена → 201.
        using (var lowerBound = await CreateLabAsync(client, semester: 1, number: 941))
        {
            using var body = await ResponseAssert.ParseWithStatusAsync(
                lowerBound, HttpStatusCode.Created, "POST /api/v1/labs (semester=1 — нижняя граница)");
            Assert.Equal(1, B03LabInputApi.ReadInt(body.RootElement, "semester"));
        }

        // when/then: semester=10 — верхняя граница включена → 201.
        using (var upperBound = await CreateLabAsync(client, semester: 10, number: 942))
        {
            using var body = await ResponseAssert.ParseWithStatusAsync(
                upperBound, HttpStatusCode.Created, "POST /api/v1/labs (semester=10 — верхняя граница)");
            Assert.Equal(10, B03LabInputApi.ReadInt(body.RootElement, "semester"));
        }

        // when/then: semester=0 и semester=11 — вне 1..10 → 400 с шаблонным текстом
        // (рендер от Labs__MaxSemester=10; единственная ошибка поля).
        foreach (var semester in new[] { 0, 11 })
        {
            using var response = await CreateLabAsync(client, semester: semester, number: 943);
            using var body = await ResponseAssert.ParseWithStatusAsync(
                response, HttpStatusCode.BadRequest, $"POST /api/v1/labs (semester={semester} — вне 1..10)");
            ResponseAssert.MessageIs(body.RootElement, "Данные заполнены неверно");
            ResponseAssert.FieldErrorsExactly(body.RootElement, "semester", "Семестр — число от 1 до 10");
        }
    }

    private static Task<HttpResponseMessage> CreateLabAsync(HttpClient client, int semester, int number) =>
        B03LabInputApi.PostAsync(
            client,
            new
            {
                number,
                semester,
                content = "Граница семестра",
                assignmentUrl = (string?)null,
                defenseRequired = false,
            });
}
