using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-091 «Профиль: границы длины email 254/255» (boundary, FR-015, P2).
///
/// given: текущий пользователь приложения — засеянный преподаватель; клиент
///        анонимный (аутентификации в приложении нет); fullName валиден.
/// when:  PUT с email длиной ровно 254 (валидный формат); затем с email длиной 255.
/// then:  первый — 200; второй — 400, errors.email=['Email — не более 254 символов']
///        (доменная модель User.email: 1–254 после трима; текст словаря email.length).
///        Оба email — валидного формата (одна «@», непустые метки домена): нарушено
///        ровно правило длины, не формат.
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts091_ProfilePutEmailLengthBoundaryTests : IClassFixture<B16WebAppFactory>
{
    private const string ValidFullName = "Студент ТС-091";

    private readonly B16WebAppFactory _factory;

    public Ts091_ProfilePutEmailLengthBoundaryTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_Email254Accepted_Email255RejectedWithLengthError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        _ = await B16Harness.CurrentUserAsync(_factory);
        using var client = B16Harness.Create(_factory);

        // email валидного формата: локальная часть из «a» + «@example.com».
        var email254 = new string('a', 242) + "@example.com";
        var email255 = new string('a', 243) + "@example.com";
        Assert.Equal(254, email254.Length);
        Assert.Equal(255, email255.Length);

        // when/then №1: email длиной ровно 254 — 200, email принят.
        using var accepted = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = ValidFullName,
            email = email254,
        });
        Assert.True(
            accepted.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 для email длиной 254, фактически {(int)accepted.StatusCode}: {await accepted.Content.ReadAsStringAsync()}");
        var acceptedRoot = await B16Assertions.ReadRootObjectAsync(accepted);
        B16Assertions.StringPropertyIs(acceptedRoot, "email", email254);

        // when/then №2: email длиной 255 — 400 с errors.email = [email.length] дословно.
        using var rejected = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = ValidFullName,
            email = email255,
        });
        Assert.True(
            rejected.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 для email длиной 255, фактически {(int)rejected.StatusCode}: {await rejected.Content.ReadAsStringAsync()}");
        var rejectedRoot = await B16Assertions.ReadRootObjectAsync(rejected);
        B16Assertions.MessageIs(rejectedRoot, ErrorTexts.InvalidData);
        B16Assertions.ErrorFieldEquals(rejectedRoot, "email", ErrorTexts.EmailLength);
    }
}
