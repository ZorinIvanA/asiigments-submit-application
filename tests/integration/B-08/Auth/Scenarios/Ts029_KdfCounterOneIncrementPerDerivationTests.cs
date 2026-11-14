using LabsApp.Auth;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-029 «KDF-счётчик: каждая деривация учитывается ровно один раз»
/// (happy_path, FR-005 + NFR-004, P0).
///
/// given: счётчик IKdfCounter обнулён.
/// when:  Hash(x); Verify(a, h); VerifyReference(b).
/// then:  сумма счётчика по всем меткам вызывателя = 3 (FR-005 AC «Счётчик на
///        каждый вызов»; NFR-004).
///
/// Метки вызывателя шагов: Hash — seed (DI-сид обязан маркироваться так же, как
/// штатный сид-путь; FR-005 «КАЖДАЯ деривация» включает сид), Verify — login,
/// VerifyReference — reference (IF-002).
/// </summary>
public sealed class Ts029_KdfCounterOneIncrementPerDerivationTests
{
    [Fact]
    public void Hash_Verify_VerifyReference_TotalCounterDelta_EqualsThree()
    {
        using var factory = new B08AuthDevFactory();

        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var counter = factory.Services.GetRequiredService<IKdfCounter>();

        // given: счётчик обнулён (базовая линия снимка).
        var before = counter.Snapshot();

        // when: Hash(x); Verify(a, h); VerifyReference(b) — три деривации.
        var storedHash = hasher.Hash("x", KdfCallers.Seed);
        var verified = hasher.Verify("a", storedHash, KdfCallers.Login);
        var reference = hasher.VerifyReference("b");

        // then: сумма счётчика по всем меткам вызывателя = 3; каждая метка —
        // ровно один раз (заголовок кейса «учитывается ровно один раз»).
        var delta = B08AuthHost.KdfDelta(before, counter.Snapshot());
        Assert.Equal(3, delta.Total());
        Assert.Equal(1, delta.GetValueOrDefault(KdfCallers.Seed));
        Assert.Equal(1, delta.GetValueOrDefault(KdfCallers.Login));
        Assert.Equal(1, delta.GetValueOrDefault(KdfCallers.Reference));

        // Детерминизм результатов шагов (санитарная часть when, не предмет then):
        // Verify с чужим кандидатом — false, VerifyReference со случайным
        // эталоном — false; сами результаты на счётчик не влияют.
        Assert.False(verified);
        Assert.False(reference);
    }
}
