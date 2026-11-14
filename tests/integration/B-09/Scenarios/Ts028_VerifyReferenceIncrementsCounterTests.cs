using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-028 «KDF-сервис: эталонная проверка инкрементирует счётчик»
/// (happy_path, FR-005 + NFR-004, P0).
///
/// given: счётчик IKdfCounter обнулён (базовой линией снимка — интерфейс
///        сброса не имеет; стандартная методика гейтов FR-027).
/// when:  VerifyReference('anything').
/// then:  возвращено false; auth_kdf_operations_total{endpoint='reference'} = 1
///        (FR-005 AC «Эталонная проверка считается»).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (файл Ts028_VerifyReferenceIncrementsCounterTests.cs;
/// копия Ts025_VerifyReferenceCounted в B-09 помечена арбитражем дубликатом).
/// Поведенческая часть кейса исполнима дословно и исполнена в собственной зоне
/// батча B-09 (прецедент c-1052); расхождение размещения зафиксировано в
/// scenario_change_requests. Существующие файлы прежней волны зоны (Ts025*)
/// не изменялись.
/// </summary>
public sealed class Ts028_VerifyReferenceIncrementsCounterTests : IClassFixture<B09WebAppFactory>
{
    private readonly B09WebAppFactory _factory;

    public Ts028_VerifyReferenceIncrementsCounterTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void VerifyReference_ReturnsFalse_AndIncrementsCounterByOne()
    {
        // given: счётчик IKdfCounter обнулён (baseline снимка до вызова).
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: VerifyReference('anything') — эталонная деривация.
        var result = B09KdfSeams.VerifyReference(hasher, "anything");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: возвращено false (результат эталонной проверки всегда отбрасывается).
        Assert.False(result, "VerifyReference должна вернуть false (результат отбрасывается).");

        // then: auth_kdf_operations_total{endpoint='reference'} = 1.
        Assert.True(
            B09KdfSeams.Delta(before, after, "reference") == 1,
            "Ожидался прирост счётчика auth_kdf_operations_total{endpoint='reference'} = 1 " +
            $"(одна эталонная деривация), фактически приращения: {B09KdfSeams.DeltaBreakdown(before, after)}.");
    }
}
