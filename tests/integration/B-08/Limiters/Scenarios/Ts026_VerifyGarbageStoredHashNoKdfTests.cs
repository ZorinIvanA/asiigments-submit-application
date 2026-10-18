using LabsApp.Auth;
using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-026 «KDF: Verify на мусорном storedHash — false без исключения и
/// без KDF» (negative, FR-005, P1). Δkdf-шов — IKdfCounter.Snapshot()
/// (IF-002, FR-027); у счётчика нет сброса, «счётчик обнулён» исполняется
/// базовой линией снимка перед шагом и разностью после (стандартная методика
/// Δkdf-гейтов).
///
/// given: сервис Verify; storedHash='garbage' (не формат pbkdf2); счётчик
///        KDF обнулён.
/// when:  Verify('x', 'garbage').
/// then:  false; исключение не выброшено; Δkdf=0 (внутренний контракт
///        IPasswordHasher/IF-002: Verify для malformed storedHash возвращает
///        false (не исключение) и KDF не выполняет).
/// </summary>
public sealed class Ts026_VerifyGarbageStoredHashNoKdfTests : IClassFixture<B08LimitersKdfWebAppFactory>
{
    private readonly B08LimitersKdfWebAppFactory _factory;

    public Ts026_VerifyGarbageStoredHashNoKdfTests(B08LimitersKdfWebAppFactory factory) => _factory = factory;

    [Fact]
    public void Verify_GarbageStoredHash_FalseWithoutExceptionAndDerivation()
    {
        // given: сервис Verify; storedHash='garbage' (не формат pbkdf2);
        // счётчик KDF обнулён — базовая линия снимка IKdfCounter.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var counter = _factory.Services.GetRequiredService<IKdfCounter>();
        var before = counter.Snapshot();

        // when: Verify('x', 'garbage') — факт выполнения без исключения
        // фиксируется завершением вызова.
        var result = hasher.Verify("x", "garbage", KdfCallers.Login);
        var after = counter.Snapshot();

        // then: false; исключение не выброшено; Δkdf=0 — ни одной деривации
        // (ни один caller не инкрементирован).
        Assert.False(result, "Verify на malformed storedHash возвращает false");
        var deltaTotal = after.Sum(pair => pair.Value - before.GetValueOrDefault(pair.Key))
            + before.Where(pair => !after.ContainsKey(pair.Key)).Sum(pair => pair.Value);
        Assert.Equal(0, deltaTotal);
    }
}
