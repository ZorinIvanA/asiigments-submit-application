using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-126 (P1, negative; FR-017) «Лабораторные: DELETE несуществующего id — 404».
/// given: случайный uuid (группы работ его не содержит — проверка по шву хранилища);
///        сессия teacher (минт, ADR-015).
/// when:  DELETE /labs/&lt;uuid&gt;.
/// then:  404 'Лабораторная не найдена' (FR-017).
/// </summary>
public sealed class Ts126_DeleteLabUnknownId404Tests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts126_DeleteLabUnknownId404Tests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteUnknownLabId_Returns404LabNotFound()
    {
        // given: случайный uuid, которого нет в хранилище работ; teacher авторизован.
        var unknownId = Guid.NewGuid();
        Assert.Null(_factory.Services.GetRequiredService<ILabRepository>().GetById(unknownId));
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: DELETE /labs/<uuid>.
        using var response = await client.DeleteAsync($"/api/v1/labs/{unknownId}");

        // then: 404 'Лабораторная не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }
}
