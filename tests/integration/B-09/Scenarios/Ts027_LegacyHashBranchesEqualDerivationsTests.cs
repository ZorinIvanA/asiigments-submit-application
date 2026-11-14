using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-027 «Legacy-хэш: равное число дериваций в ветках Verify и
/// VerifyReference» (boundary, FR-005 + FR-007, P0).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000; конфигурация
///        процесса изменена на 2000; счётчик KDF сбрасывается перед каждым
///        запросом (базовой линией снимка IKdfCounter — гейты FR-027).
/// when:  неудачный вход по этому логину (ветка Verify); неудачный вход по
///        неизвестному логину (ветка VerifyReference).
/// then:  оба — 401 'Неверный логин или пароль'; Δkdf=1 на каждый запрос;
///        расхождение времени выполнения веток НЕ утверждается
///        (документированная остаточная поверхность AR-004/ASM-020).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (файл Ts027_LegacyHashBranchesEqualDerivationsTests.cs;
/// копия Ts024_LegacyHashBranchesUnified401Kdf в B-09 помечена арбитражем
/// дубликатом). Поведенческая часть кейса исполнима дословно и исполнена в
/// собственной зоне батча B-09 (прецедент c-1052); расхождение размещения
/// зафиксировано в scenario_change_requests. Существующие файлы прежней волны
/// зоны (Ts024*) не изменялись.
/// </summary>
public sealed class Ts027_LegacyHashBranchesEqualDerivationsTests : IClassFixture<B09WebAppFactory>
{
    private const string LegacyLogin = "ts027legacy";
    private const string LegacyEmail = "ts027legacy@example.com";
    private const string RegistrationIp = "10.0.0.27";
    private const string ClientIp = "10.0.0.63";

    private const string UnknownLogin = "ts027ghost";
    private const string WrongPassword = "Wrong0rd!";

    private readonly B09WebAppFactory _factory;

    public Ts027_LegacyHashBranchesEqualDerivationsTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FailedLoginsOnLegacyHash_VerifyAndReferenceBranches_Both401WithDeltaKdf1()
    {
        // given: пользователь создан при Auth__Pbkdf2Iterations=1000, затем
        // конфигурация процесса изменена на 2000 (хранилище не пересоздано).
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Седьмой", LegacyLogin, LegacyEmail, ip: RegistrationIp);
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        using var client = B09AuthHttp.Create(_factory, ClientIp);

        // when: неудачный вход по известному логину (ветка Verify).
        var beforeKnown = B09KdfSeams.KdfSnapshot(_factory);
        using var knownLogin = await B09AuthHttp.LoginAsync(client, LegacyLogin, WrongPassword);
        var afterKnown = B09KdfSeams.KdfSnapshot(_factory);

        // when: неудачный вход по неизвестному логину (ветка VerifyReference).
        var beforeUnknown = B09KdfSeams.KdfSnapshot(_factory);
        using var unknownLogin = await B09AuthHttp.LoginAsync(client, UnknownLogin, WrongPassword);
        var afterUnknown = B09KdfSeams.KdfSnapshot(_factory);

        // then: оба — 401 'Неверный логин или пароль'.
        var knownBody = await B09Assertions.ParseObjectAsync(
            knownLogin, HttpStatusCode.Unauthorized, "неудачный вход по известному логину (ветка Verify)");
        B09Assertions.MessageIs(knownBody, B09AuthSupport.WrongCredentialsMessage);

        var unknownBody = await B09Assertions.ParseObjectAsync(
            unknownLogin, HttpStatusCode.Unauthorized, "неудачный вход по неизвестному логину (ветка VerifyReference)");
        B09Assertions.MessageIs(unknownBody, B09AuthSupport.WrongCredentialsMessage);

        // then: Δkdf=1 на каждый запрос — ветки неразличимы по числу дериваций.
        Assert.True(
            B09KdfSeams.DeltaTotal(beforeKnown, afterKnown) == 1,
            "Ветка Verify: ожидался Δkdf=1, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(beforeKnown, afterKnown)}.");
        Assert.True(
            B09KdfSeams.DeltaTotal(beforeUnknown, afterUnknown) == 1,
            "Ветка VerifyReference: ожидался Δkdf=1, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(beforeUnknown, afterUnknown)}.");

        // Расхождение времени выполнения веток НЕ утверждается — документированная
        // остаточная поверхность AR-004/ASM-020 (legacy-хэш деривирует с 1000,
        // эталон — с итерациями старта хоста).
    }
}
