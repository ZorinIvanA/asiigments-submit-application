using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-137 (P0, negative; FR-050) «GET /students: токены разных полей не матчатся».
/// given: Тот же студент (Иванов Иван Иванович 01, student01, s01@x.ru).
/// when: GET /students?search=иванов%20student01.
/// then: 200; пустая выборка ('иванов' ∈ fullName, 'student01' ∈ login — разные поля).
/// FR-050 AC «Токены разных полей не матчатся».
/// </summary>
public sealed class Ts137_StudentsSearchCrossFieldTokensTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS137_Search_TokensInDifferentFields_ReturnsEmpty()
    {
        // given: тот же студент (свой хост-фикстура класса, DI-сид).
        B12Seed.EnsureStudent(
            _factory,
            login: "student01",
            fullName: "Иванов Иван Иванович 01",
            email: "s01@x.ru");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /students?search=иванов student01 (токены из разных полей).
        using var response = await client.GetAsync(
            $"/api/v1/students?search={Uri.EscapeDataString("иванов student01")}");

        // then: 200; пустая выборка.
        var root = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
