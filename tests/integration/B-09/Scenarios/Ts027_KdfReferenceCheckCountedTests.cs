using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-027 «KDF: эталонная проверка выполняется и считается» (happy_path,
/// FR-005 + NFR-004, P0).
///
/// given: счётчик auth_kdf_operations_total обнулён (базовая линия снимка
///        IKdfCounter — тестовый шов счётчика с метками вызывателя).
/// when:  VerifyReference('anything').
/// then:  возврат false (результат всегда отбрасывается);
///        auth_kdf_operations_total{вызыватель=reference} = 1
///        (AC FR-005 «Эталонная проверка считается»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файлы прежних волн
/// зоны с совпадающим поведением (Ts025*/Ts028*) не изменялись.
/// </summary>
public sealed class Ts027_KdfReferenceCheckCountedTests : IClassFixture<B09WebAppFactory>
{
    private readonly B09WebAppFactory _factory;

    public Ts027_KdfReferenceCheckCountedTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void VerifyReference_ReturnsFalse_AndIncrementsReferenceCounterByExactlyOne()
    {
        // given: счётчик обнулён (baseline снимка тестового шва IKdfCounter).
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: VerifyReference('anything') — эталонная проверка выполняется.
        var result = B09KdfSeams.VerifyReference(hasher, "anything");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: возврат false (результат всегда отбрасывается).
        Assert.False(result, "VerifyReference всегда возвращает false (результат отбрасывается).");

        // then: auth_kdf_operations_total{вызыватель=reference} = 1 — эталонная
        // проверка считается (по всем меткам тоже ровно 1 деривация).
        Assert.True(
            B09KdfSeams.Delta(before, after, KdfCallers.Reference) == 1,
            $"Ожидался прирост auth_kdf_operations_total{{reference}} = 1, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(before, after)}.");
        Assert.True(
            B09KdfSeams.DeltaTotal(before, after) == 1,
            "Эталонная проверка обязана дать ровно одну деривацию по всем меткам, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(before, after)}.");
    }
}
