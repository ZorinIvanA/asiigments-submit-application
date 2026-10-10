using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-024 «Legacy-хэш и эталонная ветка: равные Δkdf, единый 401» (boundary, FR-005, P1).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000; в рамках одного
///        процесса конфигурация изменена на 2000 (хранилище не пересоздано);
///        тестовый шов счётчика KDF доступен; Δkdf — приращение счётчика
///        auth_kdf_operations_total между замерами до/после запроса.
/// when:  неудачный вход по этому логину (ветка Verify) и неудачный вход по
///        неизвестному логину (ветка VerifyReference).
/// then:  оба — 401 'Неверный логин или пароль'; Δkdf=1 на каждый запрос;
///        расхождение времени выполнения веток допускается и НЕ проверяется
///        (документированная остаточная поверхность AR-004/ASM-020).
/// </summary>
public sealed class Ts024_LegacyHashBranchesUnified401KdfTests : IClassFixture<B09WebAppFactory>
{
    private const string Login = "ts024";
    private const string Email = "ts024@b.ru";
    private const string RegistrationIp = "10.0.0.25";
    private const string WrongPassword = "WrongPass1!";
    private const string UnknownLogin = "ts024-unknown";

    private readonly B09WebAppFactory _factory;

    public Ts024_LegacyHashBranchesUnified401KdfTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task VerifyAndReferenceBranches_Both401_WithExactlyOneKdfEach()
    {
        // given: пользователь создан при 1000, конфигурация изменена на 2000.
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Четвёртый", Login, Email, ip: RegistrationIp);
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        var client = B09HostClients.Create(_factory);

        // when: неудачный вход по существующему логину (ветка Verify) — замер Δkdf.
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var knownFailure = await B09HostClients.LoginAsync(client, Login, WrongPassword);
        var middle = B09KdfSeams.KdfSnapshot(_factory);

        // when: неудачный вход по неизвестному логину (ветка VerifyReference).
        using var unknownFailure = await B09HostClients.LoginAsync(client, UnknownLogin, WrongPassword);
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: оба — 401 'Неверный логин или пароль'.
        var knownBody = await B09Assertions.ParseObjectAsync(
            knownFailure, HttpStatusCode.Unauthorized, "неудачный вход по существующему логину (ветка Verify)");
        B09Assertions.MessageIs(knownBody, ContractTexts.InvalidCredentials);
        var unknownBody = await B09Assertions.ParseObjectAsync(
            unknownFailure, HttpStatusCode.Unauthorized, "неудачный вход по неизвестному логину (ветка VerifyReference)");
        B09Assertions.MessageIs(unknownBody, ContractTexts.InvalidCredentials);

        // then: Δkdf=1 на каждый запрос (расхождение тайминга веток не проверяется — AR-004).
        Assert.True(
            B09KdfSeams.DeltaTotal(before, middle) == 1,
            $"Δkdf неудачного входа (ветка Verify) должен быть 1, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(before, middle)}.");
        Assert.True(
            B09KdfSeams.DeltaTotal(middle, after) == 1,
            $"Δkdf неудачного входа (ветка VerifyReference) должен быть 1, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(middle, after)}.");
    }
}
