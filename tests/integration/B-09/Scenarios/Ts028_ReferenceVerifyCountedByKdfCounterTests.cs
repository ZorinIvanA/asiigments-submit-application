using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-028 «Эталонная проверка считается счётчиком»
/// (happy_path, FR-005 + NFR-004, P0).
///
/// given: счётчик auth_kdf_operations_total обнулён (baseline снимка
///        IKdfCounter — интерфейс сброса не имеет, стандартная методика гейтов
///        FR-027: Δ между снимками).
/// when:  VerifyReference('anything').
/// then:  возвращает false; auth_kdf_operations_total по метке 'reference' = 1
///        (FR-005 AC «Эталонная проверка считается»).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts028_ReferenceVerifyCountedByKdfCounterTests : IClassFixture<B09WebAppFactory>
{
    private readonly B09WebAppFactory _factory;

    public Ts028_ReferenceVerifyCountedByKdfCounterTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void VerifyReference_ReturnsFalse_AndCountedOnceUnderReferenceLabel()
    {
        // given: счётчик auth_kdf_operations_total обнулён (baseline снимка до
        // вызова).
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: VerifyReference('anything') — эталонная деривация.
        var result = B09KdfSeams.VerifyReference(hasher, "anything");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: возвращает false (результат эталонной проверки отбрасывается).
        Assert.False(result, "VerifyReference должна вернуть false (результат отбрасывается).");

        // then: auth_kdf_operations_total по метке 'reference' = 1.
        Assert.Equal(1, B09KdfSeams.Delta(before, after, "reference"));
    }
}
