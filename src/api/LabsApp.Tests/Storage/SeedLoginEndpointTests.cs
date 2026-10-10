using System.Globalization;
using System.Net;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Auth;
using LabsApp.Tests.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Tests.Storage;

// ============================================================================
// AC T-105 на построенном хосте (FR-025 + IF-002): сид-учётки создаются
// IPasswordHasher (метка seed), поэтому вход по сид-паре проходит — 200 MeDto
// при Δkdf(login)=1 (Verify по хэшу формата IF-002, ровно 4 сегмента).
// Корневой регресс удалённого SeedPasswordHasher: ведущий '$' в хэше (5
// сегментов) → Verify классифицировал сид-хэши malformed → 401 без деривации.
// Умолчания ПРИЛОЖЕНИЯ (infrastructureDefaults: false): Development →
// Seed__DemoData=true; Seed__TeacherPassword переопределён к документированному
// умолчанию «teacher123!» (фабрика всегда подставляет свой тестовый пароль —
// поздние settings выигрывают); итерации KDF малые (FR-027).
// ============================================================================

/// <summary>Хост сценария входа сид-учётки: один на класс (IClassFixture).</summary>
public sealed class SeedLoginHost : IDisposable
{
    private readonly TestWebAppFactory _root = new(
        settings: new Dictionary<string, string?>
        {
            [SeedOptions.TeacherPasswordVariable] = SeedOptions.DefaultTeacherPassword,
            [AuthOptions.Pbkdf2IterationsVariable] = "1000",
        },
        infrastructureDefaults: false);

    private WebApplicationFactory<Program>? _factory;

    /// <summary>
    /// Хост сценария. Отключение вотчеров конфигурации
    /// (hostBuilder:reloadConfigOnChange=false — обязательный в этой среде образец
    /// <see cref="LabsApp.Tests.Auth.AuthApiFactory"/>/B03HostFactory): пер-пользовательский лимит
    /// inotify-экземпляров (128) исчерпывается параллельными WAF-хостами,
    /// IOException на старте хоста делает полный прогон недетерминированным (CR-001).
    /// </summary>
    public WebApplicationFactory<Program> Factory =>
        _factory ??= _root.WithWebHostBuilder(builder =>
            builder.UseSetting("hostBuilder:reloadConfigOnChange", "false"));

    /// <inheritdoc/>
    public void Dispose()
    {
        _factory?.Dispose();
        _root.Dispose();
    }
}

public sealed class SeedLoginEndpointTests(SeedLoginHost host) : IClassFixture<SeedLoginHost>
{
    private readonly SeedLoginHost _host = host;

    [Fact]
    public async Task Login_SeedTeacherDefaultPassword_Returns200MeDto_SingleLoginDerivation()
    {
        // given: Development, демо-сид выполнен при построении хоста
        // (Δkdf{seed} инкременты остались за снимком «before»).
        var factory = _host.Factory;
        using var client = AuthEndpointHarness.CreateClient(factory);
        var before = AuthEndpointHarness.KdfSnapshot(factory);

        // when: POST /auth/login документированной сид-парой.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            """{"login":"teacher","password":"teacher123!"}""");

        // then: 200 MeDto; ровно одна деривация с меткой login (хэш сида
        // разобран — иначе Verify дал бы false без KDF и 401).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("teacher", body.RootElement.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Teacher, body.RootElement.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("groupName").ValueKind);

        var after = AuthEndpointHarness.KdfSnapshot(factory);
        Assert.Equal(1, AuthEndpointHarness.TotalDelta(before, after));
        Assert.Equal(1, AuthEndpointHarness.CallerDelta(before, after, KdfCallers.Login));
    }

    [Fact]
    public async Task Login_SeedStudentDefaultPassword_Returns200MeDto()
    {
        // given: демо-студент из сида; окна лимитера входа не тронуты —
        // один запрос на класс, меток нет.
        var factory = _host.Factory;
        using var client = AuthEndpointHarness.CreateClient(factory);

        // when: POST /auth/login парой демо-студента.
        using var response = await AuthEndpointHarness.PostJsonAsync(
            client,
            AuthEndpointHarness.LoginEndpoint,
            """{"login":"student01","password":"student123!"}""");

        // then: 200 MeDto студента с группой сида ИК-221.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await AuthEndpointHarness.ReadJsonAsync(response);
        Assert.Equal("student01", body.RootElement.GetProperty("login").GetString());
        Assert.Equal(UserRoles.Student, body.RootElement.GetProperty("role").GetString());
        Assert.Equal("ИК-221", body.RootElement.GetProperty("groupName").GetString());
    }

    [Fact]
    public void Host_DemoSeed_TeacherPasswordHashInStorage_HasIf002Format()
    {
        // given/when: старт приложения с демо-сидом; инспекция passwordHash
        // преподавателя в хранилище хоста.
        var users = _host.Factory.Services.GetRequiredService<IUserRepository>();
        var hasher = _host.Factory.Services.GetRequiredService<IPasswordHasher>();
        var hash = users.GetByLogin("teacher")!.PasswordHash;

        // then: «pbkdf2-sha256$…», ровно 4 сегмента, строка разбирается
        // Pbkdf2PasswordHasher (Verify проходит ровно по сид-паролю).
        Assert.StartsWith(Pbkdf2PasswordHasher.AlgorithmMarker + "$", hash, StringComparison.Ordinal);
        var segments = hash.Split('$');
        Assert.Equal(4, segments.Length);
        Assert.True(
            int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations),
            $"Сегмент итераций не разобран: '{segments[1]}'.");
        Assert.Equal(1000, iterations);
        Assert.True(hash.Length <= 500);
        Assert.True(hasher.Verify(SeedOptions.DefaultTeacherPassword, hash, KdfCallers.Login));
    }
}
