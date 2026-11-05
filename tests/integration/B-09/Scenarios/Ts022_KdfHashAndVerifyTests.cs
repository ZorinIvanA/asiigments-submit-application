using LabsApp.Auth;
using Microsoft.Extensions.DependencyInjection;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-022 «KDF: хэш и проверка» (happy_path, FR-005, P0).
///
/// given: IPasswordHasher с Auth__Pbkdf2Iterations=1000.
/// when:  hash=Hash('Str0ng!pass','login'); Verify('Str0ng!pass', hash, 'login');
///        Verify('wrong', hash, 'login').
/// then:  Verify true и false соответственно; hash начинается с
///        'pbkdf2-sha256$1000$' и содержит 4 сегмента, разделённых '$'
///        (FR-005 AC «Хэш и проверка»).
/// </summary>
public sealed class Ts022_KdfHashAndVerifyTests : IClassFixture<B09WebAppFactory>
{
    private readonly B09WebAppFactory _factory;

    public Ts022_KdfHashAndVerifyTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public void Hash_CarriesIterations_VerifyTrueForCorrectFalseForWrong()
    {
        // given: IPasswordHasher с Auth__Pbkdf2Iterations=1000.
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        // when: хэширование и обе проверки.
        var hash = B09KdfSeams.Hash(hasher, "Str0ng!pass");
        var correct = B09KdfSeams.Verify(hasher, "Str0ng!pass", hash);
        var wrong = B09KdfSeams.Verify(hasher, "wrong", hash);

        // then: Verify true и false соответственно.
        Assert.True(correct, "Verify с исходным паролем должен вернуть true.");
        Assert.False(wrong, "Verify с неверным паролем должен вернуть false.");

        // then: формат хранения 'pbkdf2-sha256$<iterations>$<saltBase64>$<hashBase64>':
        // префикс 'pbkdf2-sha256$1000$' и 4 сегмента, разделённых '$'.
        Assert.True(
            hash.StartsWith("pbkdf2-sha256$1000$", StringComparison.Ordinal),
            "Хэш должен начинаться с 'pbkdf2-sha256$1000$' (FR-005: итерации из " +
            $"Auth__Pbkdf2Iterations=1000), фактически: {hash}");
        Assert.True(
            hash.Split('$').Length == 4,
            $"Хэш должен содержать 4 сегмента, разделённых '$', фактически: {hash}");
    }
}
