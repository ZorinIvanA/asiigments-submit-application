using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Хост батча B-10 для кейса TS-194 (scope: ключ IP лимитера регистраций):
/// Development и Seed__DemoData=false — наследование <see cref="B10HostFactory"/>;
/// дополнение — capture-middleware, подменяющий RemoteIpAddress КАЖДОГО запроса
/// фиксированным loopback-значением стенда и запоминающий его (шаг given
/// «известен RemoteIpAddress тестового стенда (loopback)»).
/// CR-002: TestServer (WebApplicationFactory) оставляет Connection.RemoteIpAddress
/// равным null — значение подменяется ЯВНО (образец — TestRemoteIpStartupFilter
/// зоны B-09); подстановка выполняется до всего конвейера приложения, поэтому
/// регистровый лимитер видит ровно её: IP = Connection.RemoteIpAddress, заголовок
/// X-Forwarded-For не переписывается никем (ADR-006/IF-006, ForwardedHeaders
/// не настраиваются). В sink пишется то же подставленное значение — проверки кейса
/// выполняются на равенство с подменным. Фикстура класса поднимает свежий хост —
/// лимит регистраций на IP пуст (шаг given «запросы POST /auth/register с этого IP
/// ещё не выполнялись в окне»).
/// </summary>
public sealed class B10AuthWebAppFactory : B10HostFactory
{
    /// <summary>Подменный loopback RemoteIpAddress тестового стенда (один для всех запросов).</summary>
    public static readonly IPAddress SubstituteRemoteIp = IPAddress.Loopback;

    private readonly ConcurrentQueue<string> _remoteIps = new();

    /// <summary>RemoteIpAddress всех обслуженных запросов в порядке поступления (равен подменному).</summary>
    public IReadOnlyCollection<string> ObservedRemoteIps => _remoteIps.ToArray();

    protected override bool DemoData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.AddTransient<IStartupFilter>(_ => new RemoteIpCaptureFilter(SubstituteRemoteIp, _remoteIps)));
    }

    /// <summary>
    /// Подмена и захват RemoteIpAddress соединения до остального конвейера
    /// (образец — TestRemoteIpStartupFilter зоны B-09; подмена фиксированным
    /// loopback-значением стенда).
    /// </summary>
    private sealed class RemoteIpCaptureFilter(IPAddress substitute, ConcurrentQueue<string> sink) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    context.Connection.RemoteIpAddress = substitute;
                    sink.Enqueue(context.Connection.RemoteIpAddress.ToString());
                    await pipeline();
                });

                next(app);
            };
    }

    /// <summary>Явная проверка loopback для сообщений об отказе кейса TS-194.</summary>
    public static bool IsLoopback(string remoteIp) =>
        IPAddress.TryParse(remoteIp, out var address) && IPAddress.IsLoopback(address);
}
