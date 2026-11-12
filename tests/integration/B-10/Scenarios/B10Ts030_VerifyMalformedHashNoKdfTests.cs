using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-030 «Verify для повреждённого хранимого хэша — false без KDF»
/// (negative, FR-005, P1).
///
/// given: счётчик KDF обнулён (Δ-измерение по замороженному шву IKdfCounter,
///        ADR-031); storedHash = 'garbage-not-a-hash' (строка, не разбирающаяся
///        как формат хранимой строки IF-002 «pbkdf2-sha256$…»).
/// when:  Verify('pw', 'garbage-not-a-hash') — выброшенное исключение
///        зафиксировано Record.Exception (проверка «без исключения»).
/// then:  false, без исключения; Δkdf=0 (контракт IPasswordHasher: Verify для
///        malformed storedHash возвращает false, не исключение, и KDF не
///        выполняет).
/// </summary>
[Collection(B10Ts029KdfSerialCollection.Name)]
public sealed class B10Ts030_VerifyMalformedHashNoKdfTests : IClassFixture<B10NoDemoWebAppFactory>
{
    private const string MalformedStoredHash = "garbage-not-a-hash";
    private const string Password = "pw";

    private readonly B10NoDemoWebAppFactory _factory;

    public B10Ts030_VerifyMalformedHashNoKdfTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public void VerifyWithGarbageStoredHash_ReturnsFalseWithoutExceptionOrKdf()
    {
        // given: счётчик KDF «обнулён» (Δ-измерение); хэшер разрешён до снимка —
        // деривация эталонного хэша при старте в окно измерения не попадает.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var counter = _factory.Services.GetRequiredService<IKdfCounter>();
        var before = counter.Snapshot();

        // when: Verify('pw', 'garbage-not-a-hash') — исключение не выброшено.
        var result = false;
        var exception = Record.Exception(
            () => result = B10KdfContract.Verify(
                hasher, Password, MalformedStoredHash, B10KdfContract.CallerLogin));

        // then: без исключения (контракт IPasswordHasher: malformed storedHash —
        // НЕ исключение).
        Assert.Null(exception);

        // then: false; Δkdf=0 (KDF не выполняется на malformed storedHash).
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
