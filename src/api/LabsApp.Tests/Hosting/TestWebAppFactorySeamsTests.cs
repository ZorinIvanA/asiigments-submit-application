using System.Net;
using System.Text;
using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Observability;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Unit-тесты швов и умолчаний тестового хоста (T-006, FR-027):
/// — умолчания фабрики: Auth__Pbkdf2Iterations=1000, Seed__DemoData=false,
///   Environment=Development; явные settings выигрывают; opt-out открывает
///   умолчания приложения;
/// — KDF-шов: два Snapshot() вокруг действия — Δ по меткам читается числом
///   (DI-сид и ветка отказа login — ровно 1);
/// — IP-шов: SetClientIp подставляет RemoteIpAddress соединения — ключ лимитера
///   содержит заданный IP (инспекция IRateLimitStore);
/// — TestMeterListener сопровождает KDF-шов (метрика-проекция счётчика).
/// </summary>
public sealed class TestWebAppFactorySeamsTests : IClassFixture<TestWebAppFactory>
{
    private const string LoginEndpoint = "/api/v1/auth/login";

    private readonly TestWebAppFactory _factory;

    public TestWebAppFactorySeamsTests(TestWebAppFactory factory)
    {
        _factory = factory;
    }

    // ------------------------------------------------------------------
    // Умолчания тестовой инфраструктуры
    // ------------------------------------------------------------------

    [Fact]
    public void InfrastructureDefaults_SmallKdfIterations_DemoOff_Development()
    {
        var authOptions = _factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var seedOptions = _factory.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var environment = _factory.Services.GetRequiredService<IHostEnvironment>();

        Assert.Equal(TestWebAppFactory.TestPbkdf2Iterations, authOptions.Pbkdf2Iterations);
        Assert.Equal("false", seedOptions.DemoData);
        Assert.False(seedOptions.ResolveDemoData(inDevelopment: true));
        Assert.True(environment.IsDevelopment());
    }

    [Fact]
    public void InfrastructureDefaults_DemoOff_TeacherOnly()
    {
        using var factory = new TestWebAppFactory();
        _ = factory.Services; // построение приложения: AddInMemoryStorage + SeedDatabase

        var users = factory.Services.GetRequiredService<IUserRepository>();

        Assert.NotNull(users.GetByLogin("teacher"));
        Assert.Empty(users.ListStudents());
    }

    [Fact]
    public void InfrastructureDefaults_ExplicitSettings_Win()
    {
        using var factory = new TestWebAppFactory(settings: Settings(
            (AuthOptions.Pbkdf2IterationsVariable, "210000"),
            (SeedOptions.DemoDataVariable, "true")));
        _ = factory.Services;

        var authOptions = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var seedOptions = factory.Services.GetRequiredService<IOptions<SeedOptions>>().Value;

        Assert.Equal(210_000, authOptions.Pbkdf2Iterations);
        Assert.Equal("true", seedOptions.DemoData);
        Assert.True(seedOptions.ResolveDemoData(inDevelopment: true));
    }

    [Fact]
    public void InfrastructureDefaults_OptOut_AppDefaultsVisible()
    {
        // Opt-out для тестов умолчаний ПРИЛОЖЕНИЯ: харнес не задаёт переменные.
        using var factory = new TestWebAppFactory(infrastructureDefaults: false);
        _ = factory.Services;

        var authOptions = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var seedOptions = factory.Services.GetRequiredService<IOptions<SeedOptions>>().Value;

        Assert.Equal(AuthOptions.DefaultPbkdf2Iterations, authOptions.Pbkdf2Iterations);
        Assert.Null(seedOptions.DemoData);
        Assert.False(SeedOptions.IsKnownDemoDataValue(seedOptions.DemoData));
        // Умолчание окружения (Development) применяется приложением.
        Assert.True(seedOptions.ResolveDemoData(inDevelopment: true));
    }

    // ------------------------------------------------------------------
    // KDF-шов: Δ между двумя снимками читается числом
    // ------------------------------------------------------------------

    [Fact]
    public void KdfSeam_TwoSnapshotsAroundDiSeed_DeltaIsOne()
    {
        var before = _factory.KdfSnapshot();

        TestSession.SeedStudent(_factory, new TestUserSeed { Login = NewLogin() });

        var after = _factory.KdfSnapshot();

        Assert.Equal(1L, TestWebAppFactory.KdfDelta(before, after, KdfCallers.Seed));
        // Соседние метки не затронуты действием сида.
        Assert.Equal(0L, TestWebAppFactory.KdfDelta(before, after, KdfCallers.Login));
        Assert.Equal(0L, TestWebAppFactory.KdfDelta(before, after, KdfCallers.Reference));
    }

    [Fact]
    public async Task KdfSeam_TwoSnapshotsAroundLoginRequest_DeltaIsOne()
    {
        var login = NewLogin();
        TestSession.SeedStudent(_factory, new TestUserSeed { Login = login });
        using var client = CreateAnonymousClient(_factory);

        var before = _factory.KdfSnapshot();
        using var response = await PostLoginAsync(client, login, "Wrong-Password1!");
        var after = _factory.KdfSnapshot();

        // Ветка «известный + неверный» (SEC-001/FR-027): ровно одна деривация.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1L, TestWebAppFactory.KdfDelta(before, after, KdfCallers.Login));
    }

    [Fact]
    public void KdfDelta_MissingCallerLabel_TreatedAsZero()
    {
        var before = new Dictionary<string, long>(StringComparer.Ordinal);
        var after = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [KdfCallers.Login] = 3,
        };

        Assert.Equal(3L, TestWebAppFactory.KdfDelta(before, after, KdfCallers.Login));
        Assert.Equal(0L, TestWebAppFactory.KdfDelta(before, after, KdfCallers.Seed));
    }

    // ------------------------------------------------------------------
    // IP-шов: RemoteIpAddress соединения попадает в ключ лимитера
    // ------------------------------------------------------------------

    [Fact]
    public async Task IpSeam_SetClientIp_LimiterKeyContainsIp()
    {
        var login = NewLogin();
        TestSession.SeedStudent(_factory, new TestUserSeed { Login = login });
        using var client = CreateAnonymousClient(_factory);
        TestWebAppFactory.SetClientIp(client, "10.0.0.9");

        using var response = await PostLoginAsync(client, login, "Wrong-Password1!");

        // Отказ входа фиксирует метку лимитера неудач по дословному ключу
        // «lower(trim(login))|IP» (разделитель «|» — IF-006/FR-004, BUG-001).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var store = _factory.Services.GetRequiredService<IRateLimitStore>();
        var key = LimiterKeys.FromLogin(login) + "|" + LimiterKeys.FromIp("10.0.0.9");
        Assert.True(store.TryGetMarks(RateLimitPolicies.Login, key, out var marks));
        Assert.Single(marks);
        Assert.Contains(key, store.GetKeys(RateLimitPolicies.Login));

        // Негативный ассерт BUG-001: записи по ключу с разделителем «\n» нет.
        var obsoleteKey = LimiterKeys.FromLogin(login) + "\n" + LimiterKeys.FromIp("10.0.0.9");
        Assert.False(store.TryGetMarks(RateLimitPolicies.Login, obsoleteKey, out _));
    }

    [Fact]
    public void SetClientIp_InvalidAddress_Throws()
    {
        using var client = CreateAnonymousClient(_factory);

        Assert.Throws<ArgumentException>(
            () => TestWebAppFactory.SetClientIp(client, "not-an-ip"));
    }

    [Fact]
    public void SetClientIp_NullOrReplacement_ManagesHeader()
    {
        using var client = CreateAnonymousClient(_factory);

        TestWebAppFactory.SetClientIp(client, "10.0.0.9");
        Assert.True(client.DefaultRequestHeaders.Contains(TestWebAppFactory.RemoteIpHeader));

        // Повторная установка — перезапись, а не дубликат заголовка.
        TestWebAppFactory.SetClientIp(client, "10.0.0.10");
        var values = Assert.Single(client.DefaultRequestHeaders.GetValues(TestWebAppFactory.RemoteIpHeader));
        Assert.Equal("10.0.0.10", values);

        // null — подмена снята.
        TestWebAppFactory.SetClientIp(client, null);
        Assert.False(client.DefaultRequestHeaders.Contains(TestWebAppFactory.RemoteIpHeader));
    }

    // ------------------------------------------------------------------
    // TestMeterListener (сопровождение): метрика-проекция счётчика KDF
    // ------------------------------------------------------------------

    [Fact]
    public void MeterListener_AroundDiSeed_CountsSeedDerivations()
    {
        // Метрика «labs.api» процессно-глобальна: слушатель видит хосты всех
        // параллельных коллекций, поэтому инвариант «>= 1 с меткой seed»;
        // точное число фиксирует пер-хостовый шов IKdfCounter (KdfSeam-тесты).
        using var listener = new TestMeterListener(
            TestMeterListener.DefaultMeterName,
            KdfCounter.MetricName);

        TestSession.SeedStudent(_factory, new TestUserSeed { Login = NewLogin() });

        Assert.True(
            listener.CounterIncrement(KdfCounter.MetricName, (KdfCounter.MetricCallerTag, KdfCallers.Seed)) >= 1,
            "Метрика auth_kdf_operations_total{caller=seed} не получила деривацию DI-сида.");
    }

    // ------------------------------------------------------------------
    // Помощники
    // ------------------------------------------------------------------

    private static string NewLogin() => $"seam-{Guid.NewGuid().ToString("N")[..10]}";

    private static HttpClient CreateAnonymousClient(TestWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string login, string password)
    {
        var body = $"{{\"login\":\"{login}\",\"password\":\"{password}\"}}";
        return await client.PostAsync(LoginEndpoint, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static IReadOnlyDictionary<string, string?> Settings(params (string Key, string? Value)[] items) =>
        items.ToDictionary(item => item.Key, item => item.Value);
}
