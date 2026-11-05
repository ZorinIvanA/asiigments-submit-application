namespace LabsApp.Auth.RateLimiting;

/// <summary>
/// Имена политик состояния лимитера (IF-006, матрица FR-004): namespace записей
/// <see cref="IRateLimitStore"/>. Каждый прикладной лимитер владеет собственной
/// политикой (своим окном, лимитом и словарём ключей), поэтому политики в общем
/// экземпляре хранилища не пересекаются.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Неудачные входы: 5/60с на «lower(trim(login))|IP» (ShouldBlock/RegisterFailure).</summary>
    public const string Login = "login";

    /// <summary>Регистрации: 5/3600с на IP (TryAcquire до разбора тела).</summary>
    public const string Register = "register";

    /// <summary>Запросы кода восстановления: 3/3600с на lower(trim(email)).</summary>
    public const string RecoveryRequest = "recovery_request";
}
