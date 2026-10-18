using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-027 «Verify на повреждённом хэше — false без исключения и без KDF»
/// (negative, FR-005, P1).
///
/// given: счётчик KDF обнулён (Δ-измерение по замороженному шву IKdfCounter,
///        ADR-031); storedHash = 'not-a-valid-hash' (строка, не разбирающаяся
///        как формат хранимой строки IF-002 «pbkdf2-sha256$…»).
/// when:  Verify('pw', 'not-a-valid-hash', 'login') — выброшенное исключение
///        провалило бы тест до assert-ов (проверка «без исключения»).
/// then:  false; исключение не выброшено; Δkdf=0 (контракт IPasswordHasher:
///        malformed storedHash → false без деривации).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts027_VerifyMalformedHashNoKdfTests : IClassFixture<B10NoDemoWebAppFactory>
{
    private const string MalformedStoredHash = "not-a-valid-hash";
    private const string Password = "pw";

    private readonly B10NoDemoWebAppFactory _factory;

    public Ts027_VerifyMalformedHashNoKdfTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public void VerifyWithMalformedStoredHash_ReturnsFalseWithoutExceptionOrKdf()
    {
        // given: счётчик KDF «обнулён» (Δ-измерение); хэшер разрешён до снимка —
        // деривация эталонного хэша при старте в окно измерения не попадает.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var counter = _factory.Services.GetRequiredService<IKdfCounter>();
        var before = counter.Snapshot();

        // when: Verify('pw', 'not-a-valid-hash', 'login') — исключение не выброшено.
        var result = B10KdfContract.Verify(
            hasher, Password, MalformedStoredHash, B10KdfContract.CallerLogin);

        // then: false; Δkdf=0 (malformed storedHash → false без деривации).
        Assert.False(
            result,
            "Verify с malformed storedHash должен вернуть false без деривации, получено true.");
        var deltaTotal = counter.Snapshot().Values.Sum()
            - before.Values.Sum();
        Assert.True(
            deltaTotal == 0,
            $"Δkdf на Verify с malformed storedHash должен быть 0, фактически: {deltaTotal}.");
    }
}
