using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-028 «Эталонная проверка считается счётчиком»
/// (happy_path, FR-005 + NFR-004, P0).
///
/// given: счётчик auth_kdf_operations_total обнулён (baseline снимка
///        IKdfCounter — приращение между снимками до/после).
/// when:  VerifyReference('anything').
/// then:  возвращает false; auth_kdf_operations_total по метке 'reference' = 1
///        (FR-005 AC «Эталонная проверка считается»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файлы прежних волн
/// зоны с совпадающим поведением (Ts025_VerifyReferenceCounted,
/// Ts027_KdfReferenceCheckCounted, Ts028_VerifyReferenceIncrementsCounter) не
/// изменялись.
/// </summary>
public sealed class Ts028_VerifyReferenceCountedOnceTests : IClassFixture<B09WebAppFactory>
{
    private readonly B09WebAppFactory _factory;

    public Ts028_VerifyReferenceCountedOnceTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void VerifyReference_ReturnsFalse_AndCountsExactlyOneReferenceDerivation()
    {
        // given: счётчик auth_kdf_operations_total обнулён (baseline снимка
        // тестового шва IKdfCounter).
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: VerifyReference('anything') — ровно одна эталонная деривация.
        var result = B09KdfSeams.VerifyReference(hasher, "anything");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: возвращает false (результат эталонной проверки отбрасывается).
        Assert.False(result, "VerifyReference должна вернуть false (результат отбрасывается).");

        // then: auth_kdf_operations_total по метке 'reference' = 1.
        Assert.True(
            B09KdfSeams.Delta(before, after, KdfCallers.Reference) == 1,
            "Ожидался auth_kdf_operations_total{reference} = 1 (одна эталонная деривация), " +
            $"фактически приращения: {B09KdfSeams.DeltaBreakdown(before, after)}.");
    }
}
