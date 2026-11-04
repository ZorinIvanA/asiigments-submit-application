namespace LabsApp.Storage;

/// <summary>
/// Итог узкой атомарной мутации профиля <see cref="IUserRepository.UpdateProfile"/>
/// (CR-001/SEC-001, IF-015): различает, какая из проверок не прошла, — вызывающая
/// сторона сопоставляет значения с разными статусами (409 «Пользователь с таким
/// email уже существует» / 401 «Не авторизован», IF-013). Образец —
/// <see cref="SetGroupResult"/>.
/// </summary>
public enum UpdateProfileResult
{
    /// <summary>FullName и Email записаны, ci-индекс byEmail перепривязан.</summary>
    Success,

    /// <summary>Пользователь не найден (сессия жива, запись удалена) — 401 «Не авторизован».</summary>
    UserNotFound,

    /// <summary>lower(email) занят ДРУГИМ пользователем — запись не выполнена, 409 (FR-015).</summary>
    EmailConflict,
}
