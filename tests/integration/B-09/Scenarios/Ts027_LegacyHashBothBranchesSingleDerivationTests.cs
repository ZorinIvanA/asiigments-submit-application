using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-027 «Legacy-хэш: обе ветки входа дают ровно одну деривацию (AR-004)»
/// (boundary, FR-005, P1).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000; конфигурация в
///        том же процессе изменена на 2000; счётчик KDF обнулён перед каждым
///        запросом (baseline снимка IKdfCounter).
/// when:  неудачный вход по этому известному логину (ветка Verify); отдельно
///        неудачный вход по неизвестному логину (ветка VerifyReference).
/// then:  оба — 401 'Неверный логин или пароль'; Δkdf=1 на каждый запрос;
///        тайминг веток НЕ утверждается (документированная остаточная
///        поверхность AR-004).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts027_LegacyHashBothBranchesSingleDerivationTests : IClassFixture<B09WebAppFactory>
{
    private const string LegacyLogin = "ts027-wave-legacy";
    private const string LegacyEmail = "ts027-wave-legacy@example.com";
    private const string RegistrationIp = "10.90.0.271";
    private const string ClientIp = "10.90.0.272";
    private const string UnknownLogin = "ts027-wave-unknown";
    private const string WrongPassword = "WrongPass1!";

    private readonly B09WebAppFactory _factory;

    public Ts027_LegacyHashBothBranchesSingleDerivationTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FailedLoginsOnLegacyHash_VerifyAndReferenceBranches_401WithDeltaKdfOne()
    {
        // given: пользователь создан при Auth__Pbkdf2Iterations=1000;
        // конфигурация в том же процессе изменена на 2000 (хранилище не
        // пересоздано).
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Седьмой Волна Легаси", LegacyLogin, LegacyEmail, ip: RegistrationIp);
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        using var client = B09AuthHttp.Create(_factory, ClientIp);

        // when: неудачный вход по известному логину (ветка Verify); счётчик
        // обнулён перед запросом (baseline снимка).
        var beforeVerify = B09KdfSeams.KdfSnapshot(_factory);
        using var knownFailure = await B09AuthHttp.LoginAsync(client, LegacyLogin, WrongPassword);
        var afterVerify = B09KdfSeams.KdfSnapshot(_factory);

        // when: отдельно — неудачный вход по неизвестному логину (ветка
        // VerifyReference); счётчик снова обнулён перед запросом.
        var beforeReference = B09KdfSeams.KdfSnapshot(_factory);
        using var unknownFailure = await B09AuthHttp.LoginAsync(client, UnknownLogin, WrongPassword);
        var afterReference = B09KdfSeams.KdfSnapshot(_factory);

        // then: оба — 401 'Неверный логин или пароль'.
        var knownBody = await B09Assertions.ParseObjectAsync(
            knownFailure, HttpStatusCode.Unauthorized, "неудачный вход по известному логину (ветка Verify)");
        B09Assertions.MessageIs(knownBody, B09AuthSupport.WrongCredentialsMessage);
        var unknownBody = await B09Assertions.ParseObjectAsync(
            unknownFailure, HttpStatusCode.Unauthorized, "неудачный вход по неизвестному логину (ветка VerifyReference)");
        B09Assertions.MessageIs(unknownBody, B09AuthSupport.WrongCredentialsMessage);

        // then: Δkdf=1 на каждый запрос; тайминг веток НЕ утверждается
        // (документированная остаточная поверхность AR-004).
        Assert.True(
            B09KdfSeams.DeltaTotal(beforeVerify, afterVerify) == 1,
            "Ветка Verify: ожидался Δkdf=1, фактически: " +
            B09KdfSeams.DeltaBreakdown(beforeVerify, afterVerify));
        Assert.True(
            B09KdfSeams.DeltaTotal(beforeReference, afterReference) == 1,
            "Ветка VerifyReference: ожидался Δkdf=1, фактически: " +
            B09KdfSeams.DeltaBreakdown(beforeReference, afterReference));
    }
}
