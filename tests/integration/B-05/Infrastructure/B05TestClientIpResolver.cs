using System.Net;
using LabsApp.Auth;
using Microsoft.AspNetCore.Http;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Декоратор IClientIpResolver для сценариев батча B-05: given кейсов
/// «RemoteIpAddress=10.0.0.x» (TS-163, TS-164, TS-165, TS-034) реализуется через
/// контракт IF-006 — единственный источник IP для ключей лимитеров. Заголовок
/// <see cref="TestIpHeader"/> задаёт транспортный адрес клиента (FR-080: «в
/// тестах IP задаётся настройкой connection feature харнеса»); без заголовка —
/// поведение боевого резолвера (адрес соединения TestServer; кейсы TS-168/TS-169
/// сознательно идут без заголовка — в них проверяется именно работа
/// ForwardedHeaders/resolver на транспортном адресе 127.0.0.1).
/// </summary>
public sealed class B05TestClientIpResolver : IClientIpResolver
{
    /// <summary>Заголовок с транспортным IP клиента для ключей лимитеров.</summary>
    public const string TestIpHeader = "X-B05-Test-Remote-Ip";

    private readonly IClientIpResolver _inner;

    public B05TestClientIpResolver(IClientIpResolver inner) =>
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
