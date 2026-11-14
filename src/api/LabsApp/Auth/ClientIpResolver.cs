namespace LabsApp.Auth;

/// <summary>
/// Реализация IF-006 (ADR-006): IP клиента — ТОЛЬКО Connection.RemoteIpAddress;
/// заголовки X-Forwarded-For игнорируются (обработка forwarded-заголовков вне
/// области итерации, подмена XFF с недоверенного соединения не влияет на ключи
/// лимитеров); null → ''.
/// </summary>
public sealed class ClientIpResolver : IClientIpResolver
{
    /// <inheritdoc/>
    public string GetClientIp(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }
}
