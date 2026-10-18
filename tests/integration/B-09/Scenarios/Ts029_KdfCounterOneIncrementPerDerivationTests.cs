using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-029 «KDF-счётчик: каждая деривация учитывается ровно один раз»
/// (happy_path, FR-005 + NFR-004, P0).
///
/// given: счётчик IKdfCounter обнулён (базовой линией снимка).
/// when:  Hash(x); Verify(a, h); VerifyReference(b).
/// then:  сумма счётчика по всем меткам вызывателя = 3 (FR-005 AC «Счётчик на
///        каждый вызов»; NFR-004).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (файл Ts029_KdfCounterOneIncrementPerDerivationTests.cs;
/// копия Ts026_KdfCounterPerDerivation в B-10 помечена арбитражем дубликатом).
/// Поведенческая часть кейса исполнима дословно и исполнена в собственной зоне
/// батча B-09 (прецедент c-1052); расхождение размещения зафиксировано в
/// scenario_change_requests.
/// </summary>
public sealed class Ts029_KdfCounterOneIncrementPerDerivationTests : IClassFixture<B09WebAppFactory>
{
    private const string HashedPassword = "Str0ng!pass";
    private const string WrongPassword = "Wrong0rd!";
    private const string ReferencePassword = "Anything3!";

    private readonly B09WebAppFactory _factory;

    public Ts029_KdfCounterOneIncrementPerDerivationTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void HashVerifyAndReference_EachDerivation_CountedExactlyOnce()
    {
        // given: счётчик обнулён (baseline снимка).
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: Hash(x); Verify(a, h); VerifyReference(b) — три деривации.
        var hash = B09KdfSeams.Hash(hasher, HashedPassword);
        var verified = B09KdfSeams.Verify(hasher, WrongPassword, hash);
        var reference = B09KdfSeams.VerifyReference(hasher, ReferencePassword);
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: сумма счётчика по всем меткам вызывателя = 3.
        Assert.False(verified, "Verify с неверным паролем должна вернуть false.");
        Assert.False(reference, "VerifyReference всегда возвращает false.");
        Assert.True(
            B09KdfSeams.DeltaTotal(before, after) == 3,
            "Ожидалась сумма счётчика по всем меткам = 3 (Hash + Verify + VerifyReference, " +
            "каждая деривация учитывается ровно один раз), фактически приращения: " +
            $"{B09KdfSeams.DeltaBreakdown(before, after)}.");
    }
}
