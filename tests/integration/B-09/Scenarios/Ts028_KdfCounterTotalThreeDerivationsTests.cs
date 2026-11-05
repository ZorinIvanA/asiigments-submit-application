using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-028 «KDF: счётчик инкрементируется на каждую деривацию» (happy_path,
/// FR-005 + NFR-004, P0).
///
/// given: счётчик обнулён (базовая линия снимка IKdfCounter).
/// when:  Hash(x, 'register'); Verify(a, h, 'login'); VerifyReference(b).
/// then:  сумма счётчика по всем меткам вызывателя = 3 (AC FR-005
///        «Счётчик на каждый вызов»; NFR-004).
///
/// Метки вызывателя when передаются дословно (Hash — 'register', Verify —
/// 'login'): сигнатура IF-002 строковая; оракул кейса — СУММА по всем меткам,
/// разбивка по меткам приводится в сообщении отказа. Файл текущей волны батча
/// B-09 (перенумерация кейсов): файл прежней волны с совпадающим поведением
/// (Ts029_KdfCounterOneIncrementPerDerivation) не изменялся.
/// </summary>
public sealed class Ts028_KdfCounterTotalThreeDerivationsTests : IClassFixture<B09WebAppFactory>
{
    private const string HashedPassword = "Str0ng!pass";
    private const string WrongPassword = "Wrong0rd!";
    private const string ReferencePassword = "Anything3!";

    private readonly B09WebAppFactory _factory;

    public Ts028_KdfCounterTotalThreeDerivationsTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void HashVerifyAndReference_CounterSummedOverCallerLabels_EqualsThree()
    {
        // given: счётчик обнулён (baseline снимка).
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = B09KdfSeams.KdfSnapshot(_factory);

        // when: Hash(x, 'register'); Verify(a, h, 'login'); VerifyReference(b) —
        // три деривации, каждая инкрементирует счётчик с меткой вызывателя.
        var hash = hasher.Hash(HashedPassword, KdfCallers.Register);
        var verified = hasher.Verify(WrongPassword, hash, KdfCallers.Login);
        var reference = hasher.VerifyReference(ReferencePassword);
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: сумма счётчика по всем меткам вызывателя = 3.
        Assert.False(verified, "Verify с неверным паролем возвращает false.");
        Assert.False(reference, "VerifyReference всегда возвращает false.");
        Assert.True(
            B09KdfSeams.DeltaTotal(before, after) == 3,
            "Ожидалась сумма счётчика по всем меткам = 3 (Hash + Verify + VerifyReference, " +
            "каждая деривация учитывается ровно один раз), фактически приращения: " +
            $"{B09KdfSeams.DeltaBreakdown(before, after)}.");
    }
}
