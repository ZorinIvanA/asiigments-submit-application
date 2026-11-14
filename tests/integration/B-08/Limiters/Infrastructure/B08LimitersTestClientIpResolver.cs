using System.Net;
using LabsApp.Auth;
using Microsoft.AspNetCore.Http;

namespace LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

/// <summary>
/// Декоратор IClientIpResolver волны B-08 «лимитер/матрица»: given кейсов «IP
/// тестового клиента фиксирован» (TS-019, TS-020, TS-021, TS-024) реализуется
/// через контракт IF-006 — единственный источник IP для ключей лимитеров
/// (IP = Connection.RemoteIpAddress). Заголовок <see cref="TestIpHeader"/> задаёт
/// транспортный адрес клиента; без заголовка — поведение боевого резолвера
/// (адрес соединения TestServer). ForwardedHeaders в тестовом хосте не
/// сконфигурирован, поэтому подмена эквивалентна установке RemoteIpAddress
/// соединения (механика B07TestClientIpResolver зоны B-07).
/// </summary>
public sealed class B08LimitersTestClientIpResolver : IClientIpResolver
{
    /// <summary>Заголовок с транспортным IP клиента для ключей лимитеров.</summary>
    public const string TestIpHeader = "X-B08-Limiters-Test-Remote-Ip";

    private readonly IClientIpResolver _inner;

    public B08LimitersTestClientIpResolver(IClientIpResolver inner) =>
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
