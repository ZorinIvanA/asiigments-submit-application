using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-139 (P0, boundary; FR-050) «GET /students: неизвестный uuid группы — пустая
/// выборка, не 404».
/// given: Случайный uuid; в хранилище есть студент (CR-002: иначе пустая выборка
/// неотличима от «фильтр groupId игнорируется»); сессия teacher.
/// when: GET /students?groupId=&lt;случайный-uuid&gt;.
/// then: 200, items=[], total=0 (не 404). FR-050 AC «Неизвестный uuid группы».
/// </summary>
public sealed class Ts139_StudentsUnknownGroupUuidTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS139_GroupIdUnknownUuid_ReturnsEmptyPage_Not404()
    {
        // given: случайный uuid (в хранилище такой группы нет); студент, НЕ входящий
        // в искомую группу (DI-сид, ADR-010); сессия teacher.
        var unknownGroupId = Guid.NewGuid();
        B12Seed.EnsureStudent(
            _factory,
            login: "b12s139",
            fullName: "Фильтр Неизвестной Группы Студентович",
            email: "b12s139@mail.ru");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /students?groupId=<случайный-uuid>.
        using var response = await client.GetAsync($"/api/v1/students?groupId={unknownGroupId}");

        // then: 200, items=[], total=0 — НЕ 404.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
