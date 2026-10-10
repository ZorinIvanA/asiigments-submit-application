using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-047 (P1, boundary; FR-007) «Вход: логин без учёта регистра и с
/// пробелами».
/// given: teacher/teacher123! существует; меток лимитера нет (свежий хост).
/// when:  POST {login:'  TEACHER  ', password:'teacher123!'} (сырое JSON-тело —
///        пробелы и регистр сохранены).
/// then:  200 MeDto {login:'teacher', ...} — поиск по lower(trim(login))
///        (FR-007 AC «Логин без учёта регистра и с пробелами»).
/// </summary>
public sealed class Ts047_LoginCaseInsensitiveTrimmedLoginTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS047_Login_WithPaddedUppercaseLogin_Returns200MeDto()
    {
        // given: teacher/teacher123! существует (демо-сид); меток лимитера нет
        // (свежий хост).
        _ = _factory.Services;
        using var client = HostClients.Create(_factory);

        // when: POST {login:'  TEACHER  ', password:'teacher123!'}.
        using var response = await HostClients.PostJsonAsync(
            client,
            HostClients.LoginPath,
            """{"login":"  TEACHER  ","password":"teacher123!"}""");

        // then: 200 MeDto {login:'teacher', ...} (регистр и пробелы обрезаны
        // при поиске).
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
    }
}
