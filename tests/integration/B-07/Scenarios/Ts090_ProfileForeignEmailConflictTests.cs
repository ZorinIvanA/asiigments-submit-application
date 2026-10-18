using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-090 «PUT /me/profile: чужой занятый email ci → 409» (negative, FR-021, P0).
///
/// given: email b@x.ru занят другим пользователем (регистрация в фикстуре);
///        собственная сессия пользователя с текущим fullName.
/// when:  PUT /me/profile {fullName:текущее, email:'b@x.ru'}.
/// then:  409 «Пользователь с таким email уже существует».
///        FR-021 AC «Чужой занятый email».
/// </summary>
public sealed class Ts090_ProfileForeignEmailConflictTests : IClassFixture<B07WebAppFactory>
{
    private const string OwnerLogin = "ts090owner";
    private const string OwnerEmail = "ts090owner@x.ru";
    private const string OwnerFullName = "Текущее Имя";
    private const string BusyEmail = "b@x.ru";

    private readonly B07WebAppFactory _factory;

    public Ts090_ProfileForeignEmailConflictTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_ForeignBusyEmail_Returns409DictionaryMessage()
    {
        // given: email b@x.ru занят другим пользователем.
        await HostClients.RegisterStudentAsync(
            _factory, fullName: "Другой Пользователь", login: "ts090other", email: BusyEmail);

        // given: сессия пользователя (текущее fullName известно из регистрации).
        var (client, _) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: OwnerFullName, login: OwnerLogin, email: OwnerEmail);

        // when: попытка занять чужой email.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = OwnerFullName,
            email = BusyEmail,
        });

        // then: 409 с дословным текстом словаря.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Пользователь с таким email уже существует");
    }
}
