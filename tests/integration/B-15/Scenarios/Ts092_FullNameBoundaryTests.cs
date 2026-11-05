using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-092 «Границы ФИО: 200/201 после трима» (boundary, FR-021, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); клиент
///        анонимный (аутентификации в приложении нет).
/// when:  PUT /me/profile с fullName ровно 200 символов; отдельно 201.
/// then:  200 → HTTP 200; 201 → 400 «Данные заполнены неверно»,
///        errors.fullName=['ФИО — от 1 до 200 символов'].
///        FR-021 AC «Невалидное ФИО»; словарь ошибок.
///        Изоляция методов: IClassFixture-хост и его in-memory хранилище ОБЩИЕ
///        для всех [Fact] класса — каждый метод передаёт СОБСТВЕННЫЙ текущий
///        email преподавателя, поэтому конфликта занятости email нет.
/// </summary>
public sealed class Ts092_FullNameBoundaryTests : IClassFixture<B15WebAppFactory>
{
    private const string OwnEmail = "teacher@example.com";

    private readonly B15WebAppFactory _factory;

    public Ts092_FullNameBoundaryTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FullName_Exactly200AfterTrim_ReturnsOk()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный;
        // в PUT передаётся СОБСТВЕННЫЙ текущий email.
        var current = await B15Harness.CurrentUserAsync(_factory);
        Assert.Equal(OwnEmail, current.Email);
        using var client = B15Harness.CreateClient(_factory);

        // when: fullName ровно 200 символов после трима (симметричная конструкция — CR-003).
        var fullName200 = new string('Ф', 200);
        Assert.Equal(200, fullName200.Length);
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = fullName200,
            email = OwnEmail,
        });

        // then: граница включительно — HTTP 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FullName_Exactly201AfterTrim_Returns400WithDictionaryText()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный;
        // в PUT передаётся СОБСТВЕННЫЙ текущий email.
        var current = await B15Harness.CurrentUserAsync(_factory);
        Assert.Equal(OwnEmail, current.Email);
        using var client = B15Harness.CreateClient(_factory);

        // when: fullName ровно 201 символ после трима.
        var fullName201 = new string('Ф', 201);
        Assert.Equal(201, fullName201.Length);
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = fullName201,
            email = OwnEmail,
        });

        // then: 400 с дословным message и словарной ошибкой поля fullName.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(envelope, "fullName", "ФИО — от 1 до 200 символов");
    }
}
