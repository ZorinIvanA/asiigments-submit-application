using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-136 (P0, happy_path; FR-050) «GET /students: многословный поиск в пределах одного поля».
/// given: Студент 'Иванов Иван Иванович 01', login student01, email s01@x.ru; сессия teacher.
/// when: GET /api/v1/students?search=иванов%2001.
/// then: 200; студент найден (оба токена — подстроки fullName, ci).
/// FR-050 AC «Многословный поиск одного поля».
/// </summary>
public sealed class Ts136_StudentsSearchSingleFieldTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS136_Search_TwoTokensOfFullName_FindsStudent()
    {
        // given: студент с заданными кейсом полями (DI-сид, ADR-010); сессия teacher.
        B12Seed.EnsureStudent(
            _factory,
            login: "student01",
            fullName: "Иванов Иван Иванович 01",
            email: "s01@x.ru");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /students?search=иванов 01 (оба токена — ci-подстроки fullName).
        using var response = await client.GetAsync(
            $"/api/v1/students?search={Uri.EscapeDataString("иванов 01")}");

        // then: 200; студент найден.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("student01", items[0].GetProperty("login").GetString());
        Assert.Equal("Иванов Иван Иванович 01", items[0].GetProperty("fullName").GetString());
    }
}
