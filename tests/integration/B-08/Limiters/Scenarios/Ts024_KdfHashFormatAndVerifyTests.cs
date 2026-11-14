using LabsApp.Auth;
using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-024 «KDF-сервис: формат хэша и Verify» (happy_path, FR-005, P0).
///
/// given: сервис хэширования (IPasswordHasher) с Auth__Pbkdf2Iterations=1000
///        (фикстура B08LimitersKdfWebAppFactory).
/// when:  Hash('Str0ng!pass'); затем Verify('Str0ng!pass', hash) и
///        Verify('wrong', hash).
/// then:  hash начинается с 'pbkdf2-sha256$1000$' (формат
///        'pbkdf2-sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;':
///        ровно 4 сегмента); Verify — true и false соответственно (AC FR-005
///        «Хэш и проверка»).
/// </summary>
public sealed class Ts024_KdfHashFormatAndVerifyTests : IClassFixture<B08LimitersKdfWebAppFactory>
{
    private readonly B08LimitersKdfWebAppFactory _factory;

    public Ts024_KdfHashFormatAndVerifyTests(B08LimitersKdfWebAppFactory factory) => _factory = factory;

    [Fact]
    public void Hash_VerifyCorrectAndWrong_StorageFormatCarriesIterations()
    {
        // given: сервис хэширования (IPasswordHasher) с
        // Auth__Pbkdf2Iterations=1000.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        // when: Hash('Str0ng!pass'); затем обе проверки Verify.
        var hash = hasher.Hash("Str0ng!pass", KdfCallers.Register);
        var correct = hasher.Verify("Str0ng!pass", hash, KdfCallers.Login);
        var wrong = hasher.Verify("wrong", hash, KdfCallers.Login);

        // then: hash начинается с 'pbkdf2-sha256$1000$' и имеет ровно 4
        // сегмента формата IF-002; Verify — true и false соответственно.
        Assert.True(correct, "Verify правильного пароля — true");
        Assert.False(wrong, "Verify неверного пароля — false");
        Assert.StartsWith("pbkdf2-sha256$1000$", hash, StringComparison.Ordinal);
        Assert.Equal(4, hash.Split('$').Length);
    }
}
