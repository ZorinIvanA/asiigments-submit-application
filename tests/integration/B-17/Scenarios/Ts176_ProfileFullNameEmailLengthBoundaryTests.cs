using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-176 «me/profile PUT: границы длин fullName 200/201 и email 254/255»
/// (boundary, FR-015 + FR-023, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher»); клиент анонимный (аутентификации в
///        приложении нет); тестируемые email свободны; значения:
///        fullName 'А'×200 и 'А'×201; email длиной ровно 254 символа (локальная
///        часть 242 символа + '@example.com') и ровно 255 (локальная часть 243);
///        в каждом запросе варьируется одно поле, второе валидно (email —
///        собственный 'teacher@example.com', совпадение с собой не конфликт).
/// when:  PUT /api/v1/me/profile с fullName='А'×200; отдельно с fullName='А'×201;
///        отдельно с email=ровно 254; отдельно с email=ровно 255.
/// then:  fullName=200 и email=254 — 200 с обновлённым ProfileDto (границы
///        валидны); fullName=201 — 400 'Данные заполнены неверно',
///        errors.fullName=['ФИО — от 1 до 200 символов']; email=255 — 400,
///        errors.email=['Email — не более 254 символов'] (тексты дословно из
///        словаря «Текстов ошибок полей»: fullName, email.length; FR-015:
///        «валидация как во FR-006 (errors {fullName, email})»).
/// </summary>
public sealed class Ts176_ProfileFullNameEmailLengthBoundaryTests : IClassFixture<B17WebAppFactory>
{
    private const string OwnEmail = "teacher@example.com";

    private readonly B17WebAppFactory _factory;

    public Ts176_ProfileFullNameEmailLengthBoundaryTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_FullName200Chars_AcceptedWithUpdatedDto()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        var current = await B17ProfileHost.CurrentUserAsync(_factory);
        using var client = B17ProfileHost.Create(_factory);

        var fullName200 = new string('А', 200);
        Assert.Equal(200, fullName200.Length);

        // when: PUT с fullName='А'×200 (второе поле валидно — собственный email).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = fullName200,
            email = OwnEmail,
        });

        // then: 200 с обновлённым ProfileDto (граница 200 валидна).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 для fullName длиной 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.StringPropertyIs(root, "fullName", fullName200);
        B17BodyAssertions.StringPropertyIs(root, "email", OwnEmail);
        Assert.Equal(current.Id, (await B17ProfileHost.StoredUserAsync(_factory, current.Id)).Id);
    }

    [Fact]
    public async Task PutProfile_FullName201Chars_RejectedWithFullNameLengthError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        await B17ProfileHost.CurrentUserAsync(_factory);
        using var client = B17ProfileHost.Create(_factory);

        var fullName201 = new string('А', 201);
        Assert.Equal(201, fullName201.Length);

        // when: PUT с fullName='А'×201 (второе поле валидно).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = fullName201,
            email = OwnEmail,
        });

        // then: 400 «Данные заполнены неверно»;
        // errors.fullName = ['ФИО — от 1 до 200 символов'] дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 для fullName длиной 201, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "fullName", ErrorTexts.FullNameLength);
    }

    [Fact]
    public async Task PutProfile_Email254Chars_AcceptedWithUpdatedDto()
    {
        // given: текущий пользователь — засеянный преподаватель; тестируемый
        // email свободен; клиент анонимный.
        var current = await B17ProfileHost.CurrentUserAsync(_factory);
        using var client = B17ProfileHost.Create(_factory);

        // Email валидного формата длиной ровно 254: локальная часть 242 + '@example.com'.
        var email254 = new string('a', 242) + "@example.com";
        Assert.Equal(254, email254.Length);

        // when: PUT с email=ровно 254 (второе поле валидно).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = "Ф И О",
            email = email254,
        });

        // then: 200 с обновлённым ProfileDto (граница 254 валидна).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 для email длиной 254, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.StringPropertyIs(root, "email", email254);

        // then: в хранилище email обновлён.
        var stored = await B17ProfileHost.StoredUserAsync(_factory, current.Id);
        Assert.True(
            string.Equals(stored.Email, email254, StringComparison.Ordinal),
            $"Ожидался хранимый email «{email254}», фактически «{stored.Email}».");
    }

    [Fact]
    public async Task PutProfile_Email255Chars_RejectedWithEmailLengthError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        await B17ProfileHost.CurrentUserAsync(_factory);
        using var client = B17ProfileHost.Create(_factory);

        // Email валидного формата длиной ровно 255: локальная часть 243 + '@example.com'.
        var email255 = new string('a', 243) + "@example.com";
        Assert.Equal(255, email255.Length);

        // when: PUT с email=ровно 255 (второе поле валидно).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = "Ф И О",
            email = email255,
        });

        // then: 400 «Данные заполнены неверно»;
        // errors.email = ['Email — не более 254 символов'] дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 для email длиной 255, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "email", ErrorTexts.EmailLength);
    }
}
