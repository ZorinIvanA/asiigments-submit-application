using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-025 «KDF-хэш и проверка: формат строки и true/false»
/// (happy_path, FR-005, P0).
///
/// given: IPasswordHasher с Auth__Pbkdf2Iterations=1000.
/// when:  hash = Hash('Str0ng!pass'); Verify('Str0ng!pass', hash);
///        Verify('wrong', hash).
/// then:  true и false соответственно; hash начинается с 'pbkdf2-sha256$1000$'
///        и состоит из 4 сегментов (FR-005 AC «Хэш и проверка»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// зоны с совпадающим поведением (Ts022_KdfHashAndVerify) не изменялся.
/// </summary>
public sealed class Ts025_KdfHashFormatAndVerifyTests : IClassFixture<B09WebAppFactory>
{
    private readonly B09WebAppFactory _factory;

    public Ts025_KdfHashFormatAndVerifyTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void HashCarriesIterations_VerifyTrueForCorrect_FalseForWrong()
    {
        // given: IPasswordHasher с Auth__Pbkdf2Iterations=1000.
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        // when: hash = Hash('Str0ng!pass'); Verify('Str0ng!pass', hash);
        // Verify('wrong', hash).
        var hash = B09KdfSeams.Hash(hasher, "Str0ng!pass");
        var correct = B09KdfSeams.Verify(hasher, "Str0ng!pass", hash);
        var wrong = B09KdfSeams.Verify(hasher, "wrong", hash);

        // then: true и false соответственно.
        Assert.True(correct, "Verify с исходным паролем должен вернуть true.");
        Assert.False(wrong, "Verify с неверным паролем должен вернуть false.");

        // then: hash начинается с 'pbkdf2-sha256$1000$' и состоит из 4 сегментов:
        // 'pbkdf2-sha256$<iterations>$<saltBase64>$<hashBase64>'.
        Assert.True(
            hash.StartsWith("pbkdf2-sha256$1000$", StringComparison.Ordinal),
            "Хэш должен начинаться с 'pbkdf2-sha256$1000$' (итерации из " +
            $"Auth__Pbkdf2Iterations=1000), фактически: {hash}");
        Assert.True(
            hash.Split('$').Length == 4,
            $"Хэш должен состоять из 4 сегментов, разделённых '$', фактически: {hash}");
    }
}
