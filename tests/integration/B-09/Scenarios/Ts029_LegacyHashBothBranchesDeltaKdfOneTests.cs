using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-029 «KDF: legacy-хэш — обе ветки входа с Δkdf=1 (AR-004)» (boundary,
/// FR-005 + AR-004, P1).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000; конфигурация
///        в рамках процесса изменена на 2000 (хранилище не пересоздано);
///        счётчик обнуляется перед каждым запросом (baseline снимка IKdfCounter).
/// when:  неудачный вход по этому логину (ветка Verify); отдельно — неудачный
///        вход по неизвестному логину (ветка VerifyReference).
/// then:  оба — 401 'Неверный логин или пароль'; Δkdf=1 на каждый запрос;
///        расхождение времени выполнения веток не проверяется и допускается —
///        документированная остаточная поверхность для legacy-хэшей (AC FR-005
///        «Legacy-хэш и равномерность веток (AR-004)»; IF-002: Verify читает
///        параметры из самого хэша).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файлы прежних волн
/// зоны с совпадающим поведением (Ts024*/Ts027*) не изменялись.
/// </summary>
public sealed class Ts029_LegacyHashBothBranchesDeltaKdfOneTests : IClassFixture<B09WebAppFactory>
{
    private const string Login = "ts029legacy";
    private const string Email = "ts029legacy@example.com";
    private const string RegistrationIp = "10.0.0.29";
    private const string WrongPassword = "WrongPass1!";
    private const string UnknownLogin = "ts029-unknown";

    private readonly B09WebAppFactory _factory;

    public Ts029_LegacyHashBothBranchesDeltaKdfOneTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LegacyHashVerifyBranch_AndUnknownLoginReferenceBranch_Both401_WithDeltaKdfOne()
    {
        // given: пользователь создан при 1000; конфигурация процесса изменена
        // на 2000 (хранилище не пересоздано).
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Девятый Легаси", Login, Email, ip: RegistrationIp);
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        var client = B09HostClients.Create(_factory);

        // when: неудачный вход по этому логину (ветка Verify); счётчик обнулён
        // перед запросом (baseline снимка).
        var beforeVerify = B09KdfSeams.KdfSnapshot(_factory);
        using var knownFailure = await B09HostClients.LoginAsync(client, Login, WrongPassword);
        var afterVerify = B09KdfSeams.KdfSnapshot(_factory);

        // when: отдельно — неудачный вход по неизвестному логину (ветка
        // VerifyReference); счётчик снова обнулён перед запросом.
        var beforeReference = B09KdfSeams.KdfSnapshot(_factory);
        using var unknownFailure = await B09HostClients.LoginAsync(client, UnknownLogin, WrongPassword);
        var afterReference = B09KdfSeams.KdfSnapshot(_factory);

        // then: оба — 401 'Неверный логин или пароль'.
        var knownBody = await B09Assertions.ParseObjectAsync(
            knownFailure, HttpStatusCode.Unauthorized, "неудачный вход по legacy-логину (ветка Verify)");
        B09Assertions.MessageIs(knownBody, ContractTexts.InvalidCredentials);
        var unknownBody = await B09Assertions.ParseObjectAsync(
            unknownFailure, HttpStatusCode.Unauthorized, "неудачный вход по неизвестному логину (ветка VerifyReference)");
        B09Assertions.MessageIs(unknownBody, ContractTexts.InvalidCredentials);

        // then: Δkdf=1 на каждый запрос; расхождение времени выполнения веток
        // не проверяется и допускается (AR-004 — остаточная поверхность).
        Assert.True(
            B09KdfSeams.DeltaTotal(beforeVerify, afterVerify) == 1,
            $"Δkdf запроса ветки Verify обязан быть 1, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(beforeVerify, afterVerify)}.");
        Assert.True(
            B09KdfSeams.DeltaTotal(beforeReference, afterReference) == 1,
            $"Δkdf запроса ветки VerifyReference обязан быть 1, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(beforeReference, afterReference)}.");
    }
}
