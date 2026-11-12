namespace LabsApp.Auth;

/// <summary>
/// Источник IP клиента для ключей лимитеров (IF-006, ADR-006): адрес
/// соединения — ТОЛЬКО Connection.RemoteIpAddress; forwarded-заголовки
/// (X-Forwarded-For) игнорируются — их обработка вне области итерации (подмена
/// XFF с недоверенного соединения не влияет на ключи лимитеров). Отсутствие
/// адреса → пустая строка.
/// </summary>
public interface IClientIpResolver
{
    /// <summary>Адрес соединения клиента (Connection.RemoteIpAddress); null → ''.</summary>
    string GetClientIp(HttpContext context);
}
