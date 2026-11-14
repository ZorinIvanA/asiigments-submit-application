using System.Net;
using LabsApp.Auth;
using Microsoft.AspNetCore.Http;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Декоратор IClientIpResolver для auth-сценариев батча B-07: given кейсов
/// «RemoteIpAddress=10.0.0.x» (TS-046..TS-048, TS-053) реализуется через
/// контракт IF-006 — единственный источник IP для ключей лимитеров
/// (IP = Connection.RemoteIpAddress). Заголовок <see cref="TestIpHeader"/>
/// задаёт транспортный адрес клиента (given кейсов — фиксированный IP); без
/// заголовка —
/// поведение боевого резолвера (адрес соединения TestServer). ForwardedHeaders
/// в тестовом хосте не сконфигурирован (KnownProxies пуст), поэтому подмена
/// эквивалентна установке RemoteIpAddress соединения.
/// </summary>
public sealed class B07TestClientIpResolver : IClientIpResolver
{
    /// <summary>Заголовок с транспортным IP клиента для ключей лимитеров.</summary>
    public const string TestIpHeader = "X-B07-Test-Remote-Ip";

    private readonly IClientIpResolver _inner;

    public B07TestClientIpResolver(IClientIpResolver inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc/>
    public string GetClientIp(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Headers.TryGetValue(TestIpHeader, out var raw))
        {
            var value = raw.ToString();
            // Нормализация до канонической записи адреса — как у RemoteIpAddress.
            return IPAddress.TryParse(value, out var address) ? address.ToString() : value;
        }

        return _inner.GetClientIp(context);
    }
}
