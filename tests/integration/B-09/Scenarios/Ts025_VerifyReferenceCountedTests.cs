using LabsApp.Auth;
using Microsoft.Extensions.DependencyInjection;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-025 «Эталонная проверка выполняется и считается» (happy_path, FR-005, NFR-004, P0).
///
/// given: счётчик auth_kdf_operations_total обнулён (baseline снимка — приращение
///        между снимками до/после).
/// when:  VerifyReference('anything').
/// then:  возвращает false; auth_kdf_operations_total{endpoint='reference'} = 1
///        (FR-005 AC «Эталонная проверка считается»; IF-002: метка вызывателя
///        'reference', NFR-004).
/// </summary>
public sealed class Ts025_VerifyReferenceCountedTests : IClassFixture<B09WebAppFactory>
{
    private readonly B09WebAppFactory _factory;

    public Ts025_VerifyReferenceCountedTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void VerifyReference_ReturnsFalse_AndIncrementsReferenceCounter()
    {
        // given: baseline счётчика KDF.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: VerifyReference('anything') — ровно одна деривация против эталона.
        var result = B09KdfSeams.VerifyReference(hasher, "anything");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: результат всегда отбрасывается — false.
        Assert.False(result, "VerifyReference должна вернуть false (результат верификации по эталону отбрасывается).");

        // then: auth_kdf_operations_total{endpoint='reference'} = 1 — эталонная
        // деривация считается (NFR-004: каждая деривация инкрементирует счётчик).
        Assert.True(
            B09KdfSeams.Delta(before, after, "reference") == 1,
            "Ожидался auth_kdf_operations_total{endpoint='reference'} = 1 (одна эталонная " +
            $"деривация), фактически приращения: {B09KdfSeams.DeltaBreakdown(before, after)}.");
    }
}
