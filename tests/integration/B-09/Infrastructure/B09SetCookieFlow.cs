namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Четырёхэндпойнтный auth-поток матрицы Set-Cookie NFR-007 (кейсы TS-182/TS-183):
/// register → login → refresh → logout на одном клиенте без cookie-контейнера
/// (cookies переносятся заголовком Cookie явно — «refresh_token не
/// переустанавливается» проверяется по заголовкам ответа); возвращает ВСЕ
/// экземпляры Set-Cookie каждого ответа (NFR-007/ISS-010 — матрица
/// по-экземплярно). Статусы шагов гейтятся: register 201, login 200,
/// refresh 204, logout 204. Новый файл текущей волны батча (существующая
/// инфраструктура зоны не изменяется); тело запросов — дословные JSON-строки,
/// как у механики потока прежней волны (Ts152*).
/// </summary>
public static class B09SetCookieFlow
{
    /// <summary>Свободные логин/email регистрации потока (каждый класс кейса — своя фикстура с пустым хранилищем).</summary>
    public const string LoginName = "ts182flow";
    public const string Email = "ts182flow@example.com";
    public const string FullName = "Поток Матрицы Куки";
    public const string Password = "Passw0rd!";

    /// <summary>Результат потока: экземпляры Set-Cookie четырёх ответов по порядку.</summary>
    public sealed record FlowResult(
        IReadOnlyList<B09AuthSetCookie> Register,
        IReadOnlyList<B09AuthSetCookie> Login,
        IReadOnlyList<B09AuthSetCookie> Refresh,
        IReadOnlyList<B09AuthSetCookie> Logout);

    /// <summary>Выполняет поток register → login → refresh → logout; возвращает все Set-Cookie.</summary>
    public static async Task<FlowResult> RunAsync(B09AuthWebAppFactory factory)
    {
        using var client = B09AuthSupport.CreateClient(factory);

        // register → 201, ровно 2 Set-Cookie (access + refresh).
        IReadOnlyList<B09AuthSetCookie> registerCookies;
        using (var register = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.RegisterEndpoint,
                   "{\"fullName\":\"" + FullName + "\",\"login\":\"" + LoginName + "\",\"email\":\"" + Email + "\"," +
                   "\"password\":\"" + Password + "\",\"repeatPassword\":\"" + Password + "\"}"))
        {
            B09AuthSupport.AssertStatus(register, HttpStatusCode.Created, "register потока NFR-007");
            registerCookies = B09AuthSupport.ParseSetCookie(register);
        }

        // login → 200, ровно 2 Set-Cookie (cookie регистрации переносились,
        // вход по телу; cookie ответа берутся для следующих шагов).
        IReadOnlyList<B09AuthSetCookie> loginCookies;
        using (var login = await B09AuthHttp.PostJsonAsync(client, B09AuthSupport.LoginEndpoint,
                   "{\"login\":\"" + LoginName + "\",\"password\":\"" + Password + "\"}"))
        {
            B09AuthSupport.AssertStatus(login, HttpStatusCode.OK, "login потока NFR-007");
            loginCookies = B09AuthSupport.ParseSetCookie(login);
            B09AuthHttp.SetRequestCookies(
                client, (loginCookies[0].Name, loginCookies[0].Value), (loginCookies[1].Name, loginCookies[1].Value));
        }

        // refresh → 204, Set-Cookie берутся по refresh-cookie из заголовка.
        IReadOnlyList<B09AuthSetCookie> refreshCookies;
        using (var refresh = await B09AuthHttp.PostNoBodyAsync(client, B09AuthSupport.RefreshEndpoint))
        {
            B09AuthSupport.AssertStatus(refresh, HttpStatusCode.NoContent, "refresh потока NFR-007");
            refreshCookies = B09AuthSupport.ParseSetCookie(refresh);
        }

        // logout → 204, оба сброса.
        IReadOnlyList<B09AuthSetCookie> logoutCookies;
        using (var logout = await B09AuthHttp.PostNoBodyAsync(client, B09AuthSupport.LogoutEndpoint))
        {
            B09AuthSupport.AssertStatus(logout, HttpStatusCode.NoContent, "logout потока NFR-007");
            logoutCookies = B09AuthSupport.ParseSetCookie(logout);
        }

        return new FlowResult(registerCookies, loginCookies, refreshCookies, logoutCookies);
    }
}
