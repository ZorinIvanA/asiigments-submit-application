using LabsApp.Auth;
using LabsApp.IntegrationTests.B07.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-025 «KDF-сервис: Hash/Verify и формат хранения» (happy_path, FR-005, P0).
///
/// given: IPasswordHasher с Auth__Pbkdf2Iterations=1000 (тестовое значение,
///        фикстура B07KdfWebAppFactory).
/// when:  hash=Hash('Str0ng!pass'); Verify('Str0ng!pass', hash);
///        Verify('wrong', hash).
/// then:  Verify — true и false соответственно; hash начинается со строки
///        'pbkdf2-sha256$1000$' (FR-005 AC «Хэш и проверка»).
/// </summary>
public sealed class Ts025_PasswordHasherKdfTests : IClassFixture<B07KdfWebAppFactory>
{
    private readonly B07KdfWebAppFactory _factory;

    public Ts025_PasswordHasherKdfTests(B07KdfWebAppFactory factory) => _factory = factory;

    [Fact]
    public void PasswordHasher_HashAndVerify_StorageFormatCarriesIterations()
    {
        // given: IPasswordHasher с Auth__Pbkdf2Iterations=1000.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        // when: хэширование и обе проверки.
        var hash = hasher.Hash("Str0ng!pass", KdfCallers.Register);
        var correct = hasher.Verify("Str0ng!pass", hash, KdfCallers.Login);
        var wrong = hasher.Verify("wrong", hash, KdfCallers.Login);

        // then: true и false; формат хранения 'pbkdf2-sha256$1000$…'.
        Assert.True(correct, "Verify правильного пароля — true");
        Assert.False(wrong, "Verify неверного пароля — false");
        Assert.StartsWith("pbkdf2-sha256$1000$", hash, StringComparison.Ordinal);
    }
}
