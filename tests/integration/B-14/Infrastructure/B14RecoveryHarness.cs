namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Общие шаги кейсов восстановления пароля и сброса пароля батча B-14
/// (TS-073..TS-085, TS-207): эндпойнты IF-008, дословные тексты ошибок (FR-023)
/// и given «живой код у пользователя»: живой код добывается собственным
/// POST /auth/recovery/request, значение — из [DEV-EMAIL]-записи тестового sink
/// (IF-005); пользователи создаются B14Harness.SeedUser (реальный хэш пароля).
/// </summary>
public static class B14RecoveryHarness
{
    /// <summary>POST /auth/recovery/request (IF-008; предусловие «живой код»).</summary>
    public const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";

    /// <summary>POST /auth/recovery/confirm (IF-008, FR-013).</summary>
    public const string RecoveryConfirmEndpoint = "/api/v1/auth/recovery/confirm";

    /// <summary>POST /auth/reset-password (IF-008, FR-014).</summary>
    public const string ResetPasswordEndpoint = "/api/v1/auth/reset-password";

    /// <summary>GET /auth/me (TS-085: «access B продолжает работать»).</summary>
    public const string MeEndpoint = "/api/v1/auth/me";

    /// <summary>Текст 400 CODE_REJECTED (FR-013/FR-023, дословно).</summary>
    public const string CodeRejectedMessage = "Код восстановления не подходит";

    /// <summary>Текст 400 RESET_LINK_INVALID (FR-014/FR-023, дословно).</summary>
    public const string ResetLinkInvalidMessage = "Ссылка восстановления недействительна или истекла";

    /// <summary>Текст 400 VALIDATION (FR-014/FR-023, дословно).</summary>
    public const string InvalidDataMessage = "Данные заполнены неверно";

    public static Task<HttpResponseMessage> RequestRecoveryCodeAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(RecoveryRequestEndpoint, new { email });

    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync(RecoveryConfirmEndpoint, new { email, code });

    public static Task<HttpResponseMessage> ResetPasswordAsync(
        HttpClient client,
        string resetToken,
        string password,
        string confirmPassword) =>
        client.PostAsJsonAsync(ResetPasswordEndpoint, new { resetToken, password, confirmPassword });

    /// <summary>
    /// given «живой код у email»: POST /auth/recovery/request → 200 (предусловие),
    /// затем извлечение 6-значного кода из [DEV-EMAIL]-записи тестового sink.
    /// </summary>
    public static async Task<string> RequestLiveCodeAsync(
        B14RecoveryWebAppFactory factory,
        HttpClient client,
        string email)
    {
        using var response = await RequestRecoveryCodeAsync(client, email);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: POST /auth/recovery/request → 200, фактически " +
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return factory.LogSink.GetLastRecoveryCodeForEmail(email);
    }
}
