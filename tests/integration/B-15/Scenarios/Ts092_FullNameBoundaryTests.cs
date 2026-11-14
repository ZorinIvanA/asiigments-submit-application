using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-092 «Границы ФИО: 200/201 после трима» (boundary, FR-021, P1).
///
/// given: пользователь role=student создан прямым DI-сидом в IUserRepository
///        тестового хоста; сессия — cookie access_token с JWT HS256, минтым
///        харнесом ключом Auth__JwtKey тестового хоста; POST /auth/register и
///        POST /auth/login в предусловиях НЕ вызываются (CR-001, арбитраж a-017).
/// when:  PUT /me/profile с fullName ровно 200 символов; отдельно 201.
/// then:  200 → HTTP 200; 201 → 400 «Данные заполнены неверно»,
///        errors.fullName=['ФИО — от 1 до 200 символов'].
///        FR-021 AC «Невалидное ФИО»; словарь ошибок.
///        Изоляция методов (реворк-правка CR-001): IClassFixture-хост и его
///        in-memory хранилище ОБЩИЕ для всех [Fact] класса, а IUserRepository.Add
///        бросает StorageConflictException на повторных ci-ключах login/email —
///        идентичности сид-пользователей уникальны на каждый метод.
/// </summary>
public sealed class Ts092_FullNameBoundaryTests : IClassFixture<B15WebAppFactory>
{
    private readonly B15WebAppFactory _factory;

    public Ts092_FullNameBoundaryTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FullName_Exactly200AfterTrim_ReturnsOk()
    {
        // given: студент (DI-сид, уникальная идентичность метода — CR-001) и сессия;
        // в PUT передаётся СОБСТВЕННЫЙ текущий email студента.
        const string login = "ts092-len200";
        const string ownEmail = "ts092-len200@x.ru";
        var seeded = B15Harness.SeedStudent(_factory, fullName: "Текущее Имя", login: login, email: ownEmail);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

        // when: fullName ровно 200 символов после трима (симметричная конструкция — CR-003).
        var fullName200 = new string('Ф', 200);
        Assert.Equal(200, fullName200.Length);
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = fullName200,
            email = ownEmail,
        });

        // then: граница включительно — HTTP 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FullName_Exactly201AfterTrim_Returns400WithDictionaryText()
    {
        // given: студент (DI-сид, уникальная идентичность метода — CR-001) и сессия;
        // в PUT передаётся СОБСТВЕННЫЙ текущий email студента.
        const string login = "ts092-len201";
        const string ownEmail = "ts092-len201@x.ru";
        var seeded = B15Harness.SeedStudent(_factory, fullName: "Текущее Имя", login: login, email: ownEmail);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

        // when: fullName ровно 201 символ после трима.
        var fullName201 = new string('Ф', 201);
        Assert.Equal(201, fullName201.Length);
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = fullName201,
            email = ownEmail,
        });

        // then: 400 с дословным message и словарной ошибкой поля fullName.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(envelope, "fullName", "ФИО — от 1 до 200 символов");
    }
}
