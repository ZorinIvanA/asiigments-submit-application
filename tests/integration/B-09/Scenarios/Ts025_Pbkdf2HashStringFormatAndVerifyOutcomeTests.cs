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
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts025_Pbkdf2HashStringFormatAndVerifyOutcomeTests : IClassFixture<B09WebAppFactory>
{
    private const string Password = "Str0ng!pass";

    private readonly B09WebAppFactory _factory;

    public Ts025_Pbkdf2HashStringFormatAndVerifyOutcomeTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void HashHasPbkdf2Format_VerifyTrueForPassword_FalseForWrong()
    {
        // given: IPasswordHasher с Auth__Pbkdf2Iterations=1000.
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        // when: hash = Hash('Str0ng!pass'); Verify('Str0ng!pass', hash);
        // Verify('wrong', hash).
        var hash = B09KdfSeams.Hash(hasher, Password);
        var verifiedCorrect = B09KdfSeams.Verify(hasher, Password, hash);
        var verifiedWrong = B09KdfSeams.Verify(hasher, "wrong", hash);

        // then: true и false соответственно.
        Assert.True(verifiedCorrect, "Verify с исходным паролем должен вернуть true.");
        Assert.False(verifiedWrong, "Verify с неверным паролем должен вернуть false.");

        // then: hash начинается с 'pbkdf2-sha256$1000$' и состоит из 4 сегментов:
        // 'pbkdf2-sha256$<iterations>$<saltBase64>$<hashBase64>'.
        Assert.True(
            hash.StartsWith("pbkdf2-sha256$1000$", StringComparison.Ordinal),
            $"Хэш должен начинаться с 'pbkdf2-sha256$1000$' (итерации из Auth__Pbkdf2Iterations=1000), фактически: {hash}");
        Assert.Equal(4, hash.Split('$').Length);
    }
}
